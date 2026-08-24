using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using ShowtimeBackend.Common;
using ShowtimeBackend.Data;
using ShowtimeBackend.DTOs.OrderTicket;
using ShowtimeBackend.Entities.OrderTicket;
using ShowtimeBackend.Entities.SeatZone;
using ShowtimeBackend.Entities.ShowSession;
using ShowtimeBackend.Services.OrderTicket;

namespace ShowtimeBackend.Tests.OrderTicket;

public sealed class RefundConcurrencyTests
{
    [Fact]
    public async Task CreateAsync_WhenTicketBecomesUsedAfterQuote_DoesNotOverwriteIt()
    {
        await using var connection = await OpenConnectionAsync("Data Source=:memory:");
        await SeedIssuedOrderAsync(connection);
        var mutation = new TrackedLoadMutationInterceptor(
            (competingDb, cancellationToken) => competingDb.Database.ExecuteSqlRawAsync(
                "UPDATE E_TICKET SET TICKET_STATUS = 'USED' WHERE ORDER_ITEM_ID = 101;",
                cancellationToken));
        await using var db = CreateDbContext(connection, mutation);
        var coordinator = new ArmedMutationRefundLockCoordinator(db, mutation);

        var result = await CreateService(db, coordinator).CreateAsync(
            7,
            "alice",
            11,
            new CreateRefundRequest([101], "行程变更"),
            CancellationToken.None);

        Assert.True(mutation.Mutated);
        Assert.False(result.IsSuccess);
        Assert.Equal(OrderTicketFailure.Conflict, result.Failure);
        Assert.Equal("REFUND_TICKET_NOT_UNUSED", result.ErrorCode);
        await AssertOriginalStateAsync(connection);
    }

    [Fact]
    public async Task CreateAsync_WhenItemBecomesNonNormalAfterQuote_DoesNotOverwriteIt()
    {
        await using var connection = await OpenConnectionAsync("Data Source=:memory:");
        await SeedIssuedOrderAsync(connection);
        var mutation = new TrackedLoadMutationInterceptor(
            (competingDb, cancellationToken) => competingDb.Database.ExecuteSqlRawAsync(
                "UPDATE ORDER_ITEM SET ITEM_STATUS = 'EXCHANGING' WHERE ORDER_ITEM_ID = 101;",
                cancellationToken));
        await using var db = CreateDbContext(connection, mutation);
        var coordinator = new ArmedMutationRefundLockCoordinator(db, mutation);

        var result = await CreateService(db, coordinator).CreateAsync(
            7,
            "alice",
            11,
            new CreateRefundRequest([101], "行程变更"),
            CancellationToken.None);

        Assert.True(mutation.Mutated);
        Assert.False(result.IsSuccess);
        Assert.Equal(OrderTicketFailure.Conflict, result.Failure);
        Assert.Equal("REFUND_ITEM_NOT_ELIGIBLE", result.ErrorCode);
        await AssertOriginalStateAsync(connection);
    }

    [Fact]
    public async Task OracleRefundLockCoordinator_RequiresExistingTransaction()
    {
        await using var connection = await OpenConnectionAsync("Data Source=:memory:");
        await SeedIssuedOrderAsync(connection);
        await using var db = CreateDbContext(connection);
        var coordinator = new OracleRefundLockCoordinator(db);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => coordinator.LockOrderAsync(11, CancellationToken.None));

        Assert.Contains("transaction", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateAsync_WhenSaveFails_RollsBackAllWritesAndClearsTracker()
    {
        await using var connection = await OpenConnectionAsync("Data Source=:memory:");
        await SeedIssuedOrderAsync(connection);
        var interceptor = new ThrowingSaveInterceptor(
            () => new DbUpdateException(
                "Save failed.",
                new InvalidOperationException("simulated storage failure")));
        await using var db = CreateDbContext(connection, interceptor);

        var result = await CreateService(db).CreateAsync(
            7,
            "alice",
            11,
            new CreateRefundRequest([101], "行程变更"),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(OrderTicketFailure.Internal, result.Failure);
        Assert.Equal("REFUND_CREATE_FAILED", result.ErrorCode);
        Assert.Equal(1, interceptor.CallCount);
        Assert.Empty(db.ChangeTracker.Entries());
        Assert.Null(db.Database.CurrentTransaction);
        await AssertOriginalStateAsync(connection);
    }

    [Fact]
    public async Task CreateAsync_WhenLaterDmlFails_RollsBackPreviouslyExecutedDml()
    {
        await using var connection = await OpenConnectionAsync("Data Source=:memory:");
        await SeedIssuedOrderAsync(connection);
        var interceptor = new FailAfterFirstRefundDmlInterceptor();
        await using var db = CreateSingleCommandDbContext(connection, interceptor);
        db.Database.AutoTransactionBehavior = AutoTransactionBehavior.Never;

        var result = await CreateService(db).CreateAsync(
            7,
            "alice",
            11,
            new CreateRefundRequest([101], "行程变更"),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(OrderTicketFailure.Internal, result.Failure);
        Assert.Equal("REFUND_CREATE_FAILED", result.ErrorCode);
        Assert.Equal(1, interceptor.SuccessfulDmlCount);
        Assert.True(interceptor.AttemptedDmlCount >= 2);
        Assert.Empty(db.ChangeTracker.Entries());
        Assert.Null(db.Database.CurrentTransaction);
        await AssertOriginalStateAsync(connection);
    }

    [Fact]
    public async Task CreateAsync_WhenSecondContextChangesTicketConcurrencyToken_RollsBackAndReturnsConflict()
    {
        await using var connection = await OpenConnectionAsync("Data Source=:memory:");
        await SeedIssuedOrderAsync(connection);
        var interceptor = new CompetingTicketStatusInterceptor();
        await using var db = CreateDbContext(connection, interceptor);

        var result = await CreateService(db).CreateAsync(
            7,
            "alice",
            11,
            new CreateRefundRequest([101], "行程变更"),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(OrderTicketFailure.Conflict, result.Failure);
        Assert.Equal("REFUND_CREATE_CONFLICT", result.ErrorCode);
        Assert.True(interceptor.Mutated);
        Assert.Equal(1, interceptor.RowsAffected);
        Assert.Empty(db.ChangeTracker.Entries());
        Assert.Null(db.Database.CurrentTransaction);
        await AssertOriginalStateAsync(connection);
    }

    [Fact]
    public async Task CreateAsync_WhenRefundNumberCollides_DoesNotMisclassifyOrRetry()
    {
        await using var connection = await OpenConnectionAsync("Data Source=:memory:");
        await SeedIssuedOrderAsync(connection);
        var interceptor = new ThrowingSaveInterceptor(
            () => new DbUpdateException(
                "Save failed.",
                new InvalidOperationException(
                    "ORA-00001: unique constraint (APP_OWNER.UK_REFUND_NO) violated")));
        await using var db = CreateDbContext(connection, interceptor);

        var result = await CreateService(db).CreateAsync(
            7,
            "alice",
            11,
            new CreateRefundRequest([101], "行程变更"),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(OrderTicketFailure.Internal, result.Failure);
        Assert.Equal("REFUND_CREATE_FAILED", result.ErrorCode);
        Assert.Equal(1, interceptor.CallCount);
        Assert.Empty(db.ChangeTracker.Entries());
        await AssertOriginalStateAsync(connection);
    }

    [Fact]
    public async Task CreateAsync_WhenOrderItemConstraintCollides_ReturnsDuplicateConflict()
    {
        await using var connection = await OpenConnectionAsync("Data Source=:memory:");
        await SeedIssuedOrderAsync(connection);
        var interceptor = new ThrowingSaveInterceptor(
            () => new DbUpdateException(
                "Save failed.",
                new InvalidOperationException(
                    "ORA-00001: unique constraint (APP_OWNER.UK_REFUND_ORDER_ITEM) violated")));
        await using var db = CreateDbContext(connection, interceptor);

        var result = await CreateService(db).CreateAsync(
            7,
            "alice",
            11,
            new CreateRefundRequest([101], "行程变更"),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(OrderTicketFailure.Conflict, result.Failure);
        Assert.Equal("REFUND_ITEM_ALREADY_REQUESTED", result.ErrorCode);
        Assert.Equal(1, interceptor.CallCount);
        Assert.Empty(db.ChangeTracker.Entries());
        await AssertOriginalStateAsync(connection);
    }

    [Fact]
    public async Task TwoContextsWithStaleQuotes_WhenSubmittedSequentially_SecondReturnsDuplicateConflict()
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            $"showtime-refund-{Guid.NewGuid():N}.db");
        try
        {
            await using (var setupConnection = await OpenConnectionAsync(
                $"Data Source={databasePath}"))
            {
                await EnableWriteAheadLoggingAsync(setupConnection);
                await SeedIssuedOrderAsync(setupConnection);
            }

            await using var firstConnection = await OpenConnectionAsync(
                $"Data Source={databasePath}");
            await using var firstDb = CreateDbContext(firstConnection);
            await using var secondConnection = await OpenConnectionAsync(
                $"Data Source={databasePath}");
            await using var secondDb = CreateDbContext(secondConnection);
            var firstService = CreateService(firstDb);
            var secondService = CreateService(secondDb);
            var firstQuote = await firstService.QuoteAsync(
                7,
                11,
                new RefundQuoteRequest([101]),
                CancellationToken.None);
            var secondQuote = await secondService.QuoteAsync(
                7,
                11,
                new RefundQuoteRequest([101]),
                CancellationToken.None);
            Assert.True(firstQuote.IsSuccess);
            Assert.True(secondQuote.IsSuccess);

            var firstResult = await firstService.CreateAsync(
                7,
                "alice-1",
                11,
                new CreateRefundRequest([101], "首次提交"),
                CancellationToken.None);
            var secondResult = await secondService.CreateAsync(
                7,
                "alice-2",
                11,
                new CreateRefundRequest([101], "重复提交"),
                CancellationToken.None);

            Assert.True(firstResult.IsSuccess);
            Assert.False(secondResult.IsSuccess);
            Assert.Equal(OrderTicketFailure.Conflict, secondResult.Failure);
            Assert.Equal("REFUND_ITEM_ALREADY_REQUESTED", secondResult.ErrorCode);

            await using var verificationConnection = await OpenConnectionAsync(
                $"Data Source={databasePath}");
            await using var verificationDb = CreateDbContext(verificationConnection);
            Assert.Equal(1, await verificationDb.Set<RefundRequest>().CountAsync());
            Assert.Equal(1, await verificationDb.Set<RefundItem>().CountAsync());
            Assert.Equal(
                "REFUNDING",
                (await verificationDb.Set<OrderItem>().SingleAsync()).ItemStatus);
            Assert.Equal(
                "REFUNDING",
                (await verificationDb.Set<ETicket>().SingleAsync()).TicketStatus);
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    private static async Task<SqliteConnection> OpenConnectionAsync(
        string connectionString)
    {
        var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys = OFF;";
        await command.ExecuteNonQueryAsync();
        return connection;
    }

    private static async Task EnableWriteAheadLoggingAsync(SqliteConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode = WAL;";
        await command.ExecuteNonQueryAsync();
    }

    private static AppDbContext CreateDbContext(
        SqliteConnection connection,
        params IInterceptor[] interceptors)
    {
        var options = new DbContextOptionsBuilder<SqliteAuthDbContext>()
            .UseSqlite(connection);
        if (interceptors.Length > 0)
        {
            options.AddInterceptors(interceptors);
        }

        return new SqliteAuthDbContext(options.Options);
    }

    private static AppDbContext CreateSingleCommandDbContext(
        SqliteConnection connection,
        params IInterceptor[] interceptors)
    {
        var options = new DbContextOptionsBuilder<SqliteAuthDbContext>()
            .UseSqlite(connection, sqlite => sqlite.MaxBatchSize(1));
        if (interceptors.Length > 0)
        {
            options.AddInterceptors(interceptors);
        }

        return new SqliteAuthDbContext(options.Options);
    }

    private static async Task SeedIssuedOrderAsync(SqliteConnection connection)
    {
        await using var db = CreateDbContext(connection);
        await db.Database.EnsureCreatedAsync();
        db.Add(new ShowSession
        {
            SessionId = 21,
            ShowId = 90,
            SeatMapId = 30,
            StartTime = RefundTestData.FixedUtcNow.AddDays(3),
            EndTime = RefundTestData.FixedUtcNow.AddDays(3).AddHours(2),
            SaleStartTime = RefundTestData.FixedUtcNow.AddMonths(-1),
            SaleEndTime = RefundTestData.FixedUtcNow.AddDays(2),
            SessionStatus = "ONSALE",
        });
        db.Add(new Order
        {
            OrderId = 11,
            OrderNo = "ORD000011",
            UserId = 7,
            SessionId = 21,
            TotalAmount = 105m,
            TicketCount = 1,
            OrderStatus = "ISSUED",
            ExpireTime = RefundTestData.FixedUtcNow.AddHours(-1),
            PayTime = RefundTestData.FixedUtcNow.AddHours(-2),
            IssueTime = RefundTestData.FixedUtcNow.AddHours(-1),
            Source = "WEB",
        });
        db.Add(new Payment
        {
            PaymentId = 31,
            PaymentNo = "PAY000031",
            OrderId = 11,
            UserId = 7,
            PayAmount = 105m,
            PayChannel = "ALIPAY",
            PayStatus = "SUCCESS",
            PayTime = RefundTestData.FixedUtcNow.AddHours(-2),
        });
        db.Add(new OrderItem
        {
            OrderItemId = 101,
            OrderId = 11,
            SeatId = 501,
            PriceStrategyId = 601,
            UnitPrice = 105m,
            ItemStatus = "NORMAL",
        });
        db.Add(new ETicket
        {
            ETicketId = 201,
            ETicketNo = "TKT000201",
            OrderItemId = 101,
            UserId = 7,
            QrCode = "qr-201",
            AntiFakeCode = "anti-201",
            TicketStatus = "UNUSED",
        });
        db.Add(new SeatReservation
        {
            SeatReservationId = 301,
            SessionId = 21,
            SeatId = 501,
            OrderItemId = 101,
            ReservationType = "ORDER",
            ReservationStatus = "ACTIVE",
            ReserveTime = RefundTestData.FixedUtcNow.AddHours(-3),
        });
        db.Add(new RefundPolicy
        {
            PolicyId = 801,
            PolicyName = "全局",
            RefundDeadlineHour = 24,
            RefundRate = 1m,
            ServiceFee = 0m,
            Priority = 1,
            Status = 1,
        });
        await db.SaveChangesAsync();
    }

    private static RefundApplicationService CreateService(
        AppDbContext db,
        IRefundLockCoordinator? lockCoordinator = null) => new(
        db,
        new RefundPolicyEngine(),
        new FixedTimeProvider(RefundTestData.FixedUtcNow),
        lockCoordinator ?? new TestRefundLockCoordinator(db),
        NullLogger<RefundApplicationService>.Instance,
        new NullOrderTicketAuditSink());

    private static async Task AssertOriginalStateAsync(SqliteConnection connection)
    {
        await using var verificationDb = CreateDbContext(connection);
        Assert.Equal(0, await verificationDb.Set<RefundRequest>().CountAsync());
        Assert.Equal(0, await verificationDb.Set<RefundItem>().CountAsync());
        Assert.Equal(
            "NORMAL",
            (await verificationDb.Set<OrderItem>().SingleAsync()).ItemStatus);
        Assert.Equal(
            "UNUSED",
            (await verificationDb.Set<ETicket>().SingleAsync()).TicketStatus);
    }

    private sealed class ThrowingSaveInterceptor(
        Func<Exception> exceptionFactory) : SaveChangesInterceptor
    {
        public int CallCount { get; private set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return ValueTask.FromException<InterceptionResult<int>>(
                exceptionFactory());
        }
    }

    private sealed class CompetingTicketStatusInterceptor : SaveChangesInterceptor
    {
        private int mutated;

        public bool Mutated => Volatile.Read(ref mutated) == 1;

        public int RowsAffected { get; private set; }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.CompareExchange(ref mutated, 1, 0) != 0)
            {
                return result;
            }

            var primaryDb = (AppDbContext)eventData.Context!;
            var transaction = primaryDb.Database.CurrentTransaction ??
                throw new InvalidOperationException("Expected an active transaction.");
            var connection = (SqliteConnection)primaryDb.Database.GetDbConnection();
            var options = new DbContextOptionsBuilder<SqliteAuthDbContext>()
                .UseSqlite(connection)
                .Options;
            await using var competingDb = new SqliteAuthDbContext(options);
            await competingDb.Database.UseTransactionAsync(
                transaction.GetDbTransaction(),
                cancellationToken);
            RowsAffected = await competingDb.Database.ExecuteSqlRawAsync(
                "UPDATE E_TICKET SET TICKET_STATUS = 'USED' " +
                "WHERE ORDER_ITEM_ID = 101;",
                cancellationToken);

            return result;
        }
    }

    private sealed class FailAfterFirstRefundDmlInterceptor : DbCommandInterceptor
    {
        public int AttemptedDmlCount { get; private set; }

        public int SuccessfulDmlCount { get; private set; }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            ThrowOnLaterDml(command.CommandText);
            return ValueTask.FromResult(result);
        }

        public override ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            RecordSuccessfulDml(command.CommandText);
            return ValueTask.FromResult(result);
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            ThrowOnLaterDml(command.CommandText);
            return ValueTask.FromResult(result);
        }

        public override ValueTask<int> NonQueryExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            int result,
            CancellationToken cancellationToken = default)
        {
            RecordSuccessfulDml(command.CommandText);
            return ValueTask.FromResult(result);
        }

        private void ThrowOnLaterDml(string commandText)
        {
            if (!IsRefundApplicationDml(commandText))
            {
                return;
            }

            AttemptedDmlCount++;
            if (AttemptedDmlCount > 1)
            {
                throw new DbUpdateException(
                    "The later refund DML failed.",
                    new InvalidOperationException("simulated command failure"));
            }
        }

        private void RecordSuccessfulDml(string commandText)
        {
            if (IsRefundApplicationDml(commandText))
            {
                SuccessfulDmlCount++;
            }
        }

        private static bool IsRefundApplicationDml(string commandText)
        {
            var trimmed = commandText.TrimStart();
            if (!trimmed.StartsWith("INSERT", StringComparison.OrdinalIgnoreCase) &&
                !trimmed.StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase) &&
                !trimmed.StartsWith("DELETE", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return commandText.Contains("REFUND_REQUEST", StringComparison.OrdinalIgnoreCase) ||
                commandText.Contains("REFUND_ITEM", StringComparison.OrdinalIgnoreCase) ||
                commandText.Contains("ORDER_ITEM", StringComparison.OrdinalIgnoreCase) ||
                commandText.Contains("E_TICKET", StringComparison.OrdinalIgnoreCase);
        }
    }

    private sealed class TrackedLoadMutationInterceptor(
        Func<AppDbContext, CancellationToken, Task> mutateAsync) : DbCommandInterceptor
    {
        private int armed;
        private int mutated;

        public bool Mutated => Volatile.Read(ref mutated) == 1;

        public void Arm() => Volatile.Write(ref armed, 1);

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (Volatile.Read(ref armed) == 1 &&
                IsTrackedItemLoad(command.CommandText) &&
                Interlocked.CompareExchange(ref mutated, 1, 0) == 0)
            {
                var connection = (SqliteConnection)command.Connection!;
                var options = new DbContextOptionsBuilder<SqliteAuthDbContext>()
                    .UseSqlite(connection)
                    .Options;
                await using var competingDb = new SqliteAuthDbContext(options);
                await competingDb.Database.UseTransactionAsync(
                    command.Transaction!,
                    cancellationToken);
                await mutateAsync(competingDb, cancellationToken);
            }

            return result;
        }

        private static bool IsTrackedItemLoad(string commandText) =>
            commandText.Contains(
                "FROM \"ORDER_ITEM\"",
                StringComparison.OrdinalIgnoreCase) &&
            commandText.Contains(
                "JOIN \"E_TICKET\"",
                StringComparison.OrdinalIgnoreCase);
    }

    private sealed class ArmedMutationRefundLockCoordinator(
        AppDbContext db,
        TrackedLoadMutationInterceptor mutation) : IRefundLockCoordinator
    {
        public Task<bool> LockRefundRequestAsync(
            long refundId,
            CancellationToken cancellationToken) => db.Set<RefundRequest>()
            .AsNoTracking()
            .AnyAsync(item => item.RefundId == refundId, cancellationToken);

        public async Task<bool> LockOrderAsync(
            long orderId,
            CancellationToken cancellationToken)
        {
            var exists = await db.Set<Order>()
                .AsNoTracking()
                .AnyAsync(item => item.OrderId == orderId, cancellationToken);
            mutation.Arm();
            return exists;
        }
    }

    private sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow);
    }
}
