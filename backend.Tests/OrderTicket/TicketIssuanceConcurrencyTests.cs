using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ShowtimeBackend.Common;
using ShowtimeBackend.Common.TicketSecurity;
using ShowtimeBackend.Data;
using ShowtimeBackend.DTOs.OrderTicket;
using ShowtimeBackend.Entities.OrderTicket;
using ShowtimeBackend.Services.OrderTicket;

namespace ShowtimeBackend.Tests.OrderTicket;

public sealed class TicketIssuanceConcurrencyTests
{
    private static readonly DateTimeOffset OperationTime =
        new(2026, 8, 23, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ConcurrentCompensation_LoserReloadsCompleteOrderAsIdempotentSuccess()
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            $"showtime-ticket-{Guid.NewGuid():N}.db");
        try
        {
            await SeedIncompleteIssuedOrderAsync(databasePath);
            var secondReachedSave = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseSecondSave = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var interceptor = new BlockingSaveInterceptor(
                secondReachedSave,
                releaseSecondSave);

            await using var secondConnection = await OpenConnectionAsync(databasePath);
            await using var secondDb = CreateDbContext(secondConnection, interceptor);
            var secondService = CreateService(secondDb);
            var secondTask = secondService.IssueAsync(
                "admin-2",
                10,
                CancellationToken.None);
            await secondReachedSave.Task.WaitAsync(TimeSpan.FromSeconds(5));

            await using var firstConnection = await OpenConnectionAsync(databasePath);
            await using var firstDb = CreateDbContext(firstConnection);
            var firstResult = await CreateService(firstDb).IssueAsync(
                "admin-1",
                10,
                CancellationToken.None);
            Assert.True(firstResult.IsSuccess);
            releaseSecondSave.SetResult();

            var secondResult = await secondTask;

            Assert.True(secondResult.IsSuccess);
            Assert.Equal(0, secondResult.Value!.CreatedTicketCount);
            Assert.Equal(1, secondResult.Value.ExistingTicketCount);
            await using var verificationConnection = await OpenConnectionAsync(databasePath);
            await using var verificationDb = CreateDbContext(verificationConnection);
            Assert.Equal(1, await verificationDb.Set<ETicket>().CountAsync());
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    [Fact]
    public async Task ConcurrentPayment_LoserReturnsPersistedSuccessfulPayment()
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            $"showtime-payment-{Guid.NewGuid():N}.db");
        try
        {
            await SeedPendingOrderAsync(databasePath);
            var secondReachedSave = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseSecondSave = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var interceptor = new BlockingSaveInterceptor(
                secondReachedSave,
                releaseSecondSave);

            await using var secondConnection = await OpenConnectionAsync(databasePath);
            await using var secondDb = CreateDbContext(secondConnection, interceptor);
            var secondTask = CreatePaymentService(secondDb).PayAsync(
                7,
                "alice-2",
                10,
                new MockPaymentRequest(PaymentChannel.ALIPAY, PaymentResult.SUCCESS),
                CancellationToken.None);
            await secondReachedSave.Task.WaitAsync(TimeSpan.FromSeconds(5));

            await using var firstConnection = await OpenConnectionAsync(databasePath);
            await using var firstDb = CreateDbContext(firstConnection);
            var firstResult = await CreatePaymentService(firstDb).PayAsync(
                7,
                "alice-1",
                10,
                new MockPaymentRequest(PaymentChannel.ALIPAY, PaymentResult.SUCCESS),
                CancellationToken.None);
            Assert.True(firstResult.IsSuccess);
            releaseSecondSave.SetResult();

            var secondResult = await secondTask;

            Assert.True(secondResult.IsSuccess);
            Assert.Equal(OrderStatus.ISSUED, secondResult.Value!.OrderStatus);
            Assert.Equal(1, secondResult.Value.IssuedTicketCount);
            Assert.Equal(
                firstResult.Value!.Payment.PaymentId,
                secondResult.Value.Payment.PaymentId);
            await using var verificationConnection = await OpenConnectionAsync(databasePath);
            await using var verificationDb = CreateDbContext(verificationConnection);
            Assert.Equal(1, await verificationDb.Set<Payment>()
                .CountAsync(payment => payment.PayStatus == "SUCCESS"));
            Assert.Equal(1, await verificationDb.Set<ETicket>().CountAsync());
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    private static async Task SeedIncompleteIssuedOrderAsync(string databasePath)
    {
        await using var connection = await OpenConnectionAsync(databasePath);
        await using var db = CreateDbContext(connection);
        await db.Database.EnsureCreatedAsync();
        var order = new Order
        {
            OrderId = 10,
            OrderNo = "ORD000010",
            UserId = 7,
            SessionId = 20,
            TotalAmount = 188m,
            TicketCount = 1,
            OrderStatus = "ISSUED",
            ExpireTime = OperationTime.UtcDateTime.AddMinutes(15),
            IssueTime = OperationTime.UtcDateTime.AddDays(-1),
            Source = "WEB",
        };
        order.Items.Add(new OrderItem
        {
            OrderItemId = 1,
            OrderId = order.OrderId,
            SeatId = 100,
            PriceStrategyId = 200,
            UnitPrice = 188m,
            ItemStatus = "NORMAL",
            Order = order,
        });
        order.Payments.Add(new Payment
        {
            PaymentId = 20,
            PaymentNo = "PAY000020",
            OrderId = order.OrderId,
            UserId = order.UserId,
            PayAmount = order.TotalAmount,
            PayChannel = "ALIPAY",
            PayStatus = "SUCCESS",
            Order = order,
        });
        db.Add(order);
        await db.SaveChangesAsync();
    }

    private static async Task SeedPendingOrderAsync(string databasePath)
    {
        await using var connection = await OpenConnectionAsync(databasePath);
        await using var db = CreateDbContext(connection);
        await db.Database.EnsureCreatedAsync();
        var order = new Order
        {
            OrderId = 10,
            OrderNo = "ORD000010",
            UserId = 7,
            SessionId = 20,
            TotalAmount = 188m,
            TicketCount = 1,
            OrderStatus = "PENDING_PAY",
            ExpireTime = OperationTime.UtcDateTime.AddMinutes(15),
            Source = "WEB",
        };
        order.Items.Add(new OrderItem
        {
            OrderItemId = 1,
            OrderId = order.OrderId,
            SeatId = 100,
            PriceStrategyId = 200,
            UnitPrice = 188m,
            ItemStatus = "NORMAL",
            Order = order,
        });
        db.Add(order);
        await db.SaveChangesAsync();
    }

    private static async Task<SqliteConnection> OpenConnectionAsync(string databasePath)
    {
        var connection = new SqliteConnection($"Data Source={databasePath}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys = OFF;";
        await command.ExecuteNonQueryAsync();
        return connection;
    }

    private static AppDbContext CreateDbContext(
        SqliteConnection connection,
        SaveChangesInterceptor? interceptor = null)
    {
        var builder = new DbContextOptionsBuilder<SqliteAuthDbContext>()
            .UseSqlite(connection);
        if (interceptor is not null)
        {
            builder.AddInterceptors(interceptor);
        }

        return new SqliteAuthDbContext(builder.Options);
    }

    private static AdminTicketIssuanceService CreateService(AppDbContext db) => new(
        db,
        new FixedTimeProvider(OperationTime),
        new TicketIssuanceService(
            new HmacTicketTokenService(
                Options.Create(new TicketSecurityOptions
                {
                    SigningKeyBase64 =
                        "ERERERERERERERERERERERERERERERERERERERERERE=",
                }))),
        NullLogger<AdminTicketIssuanceService>.Instance,
        new NullOrderTicketAuditSink());

    private static PaymentService CreatePaymentService(AppDbContext db) => new(
        db,
        new FixedTimeProvider(OperationTime),
        new TicketIssuanceService(
            new HmacTicketTokenService(
                Options.Create(new TicketSecurityOptions
                {
                    SigningKeyBase64 =
                        "ERERERERERERERERERERERERERERERERERERERERERE=",
                }))),
        NullLogger<PaymentService>.Instance,
        new NullOrderTicketAuditSink());

    private sealed class BlockingSaveInterceptor(
        TaskCompletionSource reachedSave,
        TaskCompletionSource releaseSave) : SaveChangesInterceptor
    {
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            reachedSave.TrySetResult();
            await releaseSave.Task.WaitAsync(cancellationToken);
            return result;
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
