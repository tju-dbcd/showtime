using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ShowtimeBackend.Common.TicketSecurity;
using ShowtimeBackend.Data;
using ShowtimeBackend.DTOs.OrderTicket;
using ShowtimeBackend.Entities.OrderTicket;
using ShowtimeBackend.Services.OrderTicket;
using ShowSessionEntity = ShowtimeBackend.Entities.ShowSession.ShowSession;

namespace ShowtimeBackend.Tests.OrderTicket;

public sealed class TicketRedemptionConcurrencyTests
{
    private static readonly DateTimeOffset OperationTime =
        new(2026, 8, 28, 12, 0, 0, TimeSpan.Zero);
    private const string SigningKey =
        "ERERERERERERERERERERERERERERERERERERERERERE=";

    [Fact]
    public async Task StaleConcurrentRedemption_LoserReturnsAlreadyUsedWithoutOverwritingWinner()
    {
        await using var database = await SharedDatabase.CreateAsync();
        var gate = new BlockingTicketUpdateInterceptor();
        await using var staleDb = await database.CreateContextAsync(gate);
        var staleTask = CreateService(staleDb, database.TokenService).RedeemAsync(
            "admin-stale",
            new RedeemTicketRequest(database.Credential.QrCode, "gate-stale"),
            CancellationToken.None);
        await gate.UpdateReached.WaitAsync(TimeSpan.FromSeconds(5));

        await using var winnerDb = await database.CreateContextAsync();
        var winner = await CreateService(winnerDb, database.TokenService).RedeemAsync(
            "admin-winner",
            new RedeemTicketRequest(database.Credential.QrCode, "gate-winner"),
            CancellationToken.None);
        gate.Release();
        var stale = await staleTask;

        Assert.True(winner.IsSuccess);
        Assert.Equal("TICKET_ALREADY_USED", stale.ErrorCode);
        await using var verificationDb = await database.CreateContextAsync();
        var ticket = await verificationDb.Set<ETicket>().AsNoTracking().SingleAsync();
        Assert.Equal("USED", ticket.TicketStatus);
        Assert.Equal("admin-winner", ticket.CheckBy);
        Assert.Equal("gate-winner", ticket.CheckDevice);
    }

    [Theory]
    [InlineData("order", "TICKET_ORDER_NOT_ELIGIBLE")]
    [InlineData("item", "TICKET_ITEM_NOT_ELIGIBLE")]
    [InlineData("session", "TICKET_REDEMPTION_WINDOW_INVALID")]
    public async Task AggregateChangeAfterPrecheck_IsRejectedByAtomicPredicate(
        string mutation,
        string expectedCode)
    {
        await using var database = await SharedDatabase.CreateAsync();
        var gate = new BlockingTicketUpdateInterceptor();
        await using var staleDb = await database.CreateContextAsync(gate);
        var task = CreateService(staleDb, database.TokenService).RedeemAsync(
            "admin",
            new RedeemTicketRequest(database.Credential.QrCode, "gate"),
            CancellationToken.None);
        await gate.UpdateReached.WaitAsync(TimeSpan.FromSeconds(5));

        await database.MutateAsync(mutation);
        gate.Release();
        var result = await task;

        Assert.Equal(expectedCode, result.ErrorCode);
        await using var verificationDb = await database.CreateContextAsync();
        var ticket = await verificationDb.Set<ETicket>().AsNoTracking().SingleAsync();
        Assert.Equal("UNUSED", ticket.TicketStatus);
        Assert.Null(ticket.CheckTime);
        Assert.Null(ticket.CheckDevice);
        Assert.Null(ticket.CheckBy);
    }

    private static TicketRedemptionService CreateService(
        AppDbContext db,
        ITicketTokenService tokenService) => new(
        db,
        tokenService,
        new FixedTimeProvider(OperationTime),
        Options.Create(new TicketRedemptionOptions()),
        NullLogger<TicketRedemptionService>.Instance,
        new NullOrderTicketAuditSink());

    private sealed class SharedDatabase : IAsyncDisposable
    {
        private readonly string path;

        private SharedDatabase(
            string path,
            ITicketTokenService tokenService,
            TicketCredential credential)
        {
            this.path = path;
            TokenService = tokenService;
            Credential = credential;
        }

        public ITicketTokenService TokenService { get; }
        public TicketCredential Credential { get; }

        public static async Task<SharedDatabase> CreateAsync()
        {
            var path = Path.Combine(
                Path.GetTempPath(),
                $"showtime-redemption-{Guid.NewGuid():N}.db");
            var tokenService = new HmacTicketTokenService(
                Options.Create(new TicketSecurityOptions
                {
                    SigningKeyBase64 = SigningKey,
                }));
            var credential = tokenService.Generate(OperationTime);
            var database = new SharedDatabase(path, tokenService, credential);
            await using var db = await database.CreateContextAsync();
            await db.Database.EnsureCreatedAsync();
            db.AddRange(
                new ShowSessionEntity
                {
                    SessionId = 21,
                    ShowId = 90,
                    SeatMapId = 30,
                    StartTime = OperationTime.UtcDateTime.AddHours(-1),
                    EndTime = OperationTime.UtcDateTime.AddHours(1),
                    SaleStartTime = OperationTime.UtcDateTime.AddDays(-10),
                    SaleEndTime = OperationTime.UtcDateTime.AddDays(-1),
                    SessionStatus = "ENDED",
                },
                new Order
                {
                    OrderId = 11,
                    OrderNo = "ORD000011",
                    UserId = 7,
                    SessionId = 21,
                    TotalAmount = 188m,
                    TicketCount = 1,
                    OrderStatus = "ISSUED",
                    ExpireTime = OperationTime.UtcDateTime.AddDays(-1),
                    IssueTime = OperationTime.UtcDateTime.AddDays(-1),
                    Source = "WEB",
                },
                new OrderItem
                {
                    OrderItemId = 101,
                    OrderId = 11,
                    SeatId = 501,
                    PriceStrategyId = 601,
                    UnitPrice = 188m,
                    ItemStatus = "NORMAL",
                },
                new ETicket
                {
                    ETicketId = 201,
                    ETicketNo = credential.TicketNo,
                    OrderItemId = 101,
                    UserId = 7,
                    QrCode = credential.QrCode,
                    AntiFakeCode = credential.AntiFakeCode,
                    TicketStatus = "UNUSED",
                });
            await db.SaveChangesAsync();
            return database;
        }

        public async Task<AppDbContext> CreateContextAsync(
            DbCommandInterceptor? interceptor = null)
        {
            var connection = new SqliteConnection(
                $"Data Source={path};Cache=Shared;Foreign Keys=False;Default Timeout=5");
            await connection.OpenAsync();
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = "PRAGMA foreign_keys = OFF; PRAGMA busy_timeout = 5000;";
                await command.ExecuteNonQueryAsync();
            }

            var builder = new DbContextOptionsBuilder<OwnedConnectionDbContext>()
                .UseSqlite(connection);
            if (interceptor is not null)
            {
                builder.AddInterceptors(interceptor);
            }

            return new OwnedConnectionDbContext(builder.Options, connection);
        }

        public async Task MutateAsync(string mutation)
        {
            await using var connection = new SqliteConnection(
                $"Data Source={path};Cache=Shared;Foreign Keys=False;Default Timeout=5");
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = mutation switch
            {
                "order" => "UPDATE T_ORDER SET ORDER_STATUS = 'PAID' WHERE ORDER_ID = 11;",
                "item" => "UPDATE ORDER_ITEM SET ITEM_STATUS = 'REFUNDING' WHERE ORDER_ITEM_ID = 101;",
                "session" => "UPDATE SHOW_SESSION SET START_TIME = '2026-08-29 12:00:00', END_TIME = '2026-08-29 14:00:00' WHERE SESSION_ID = 21;",
                _ => throw new ArgumentOutOfRangeException(nameof(mutation), mutation, null),
            };
            Assert.Equal(1, await command.ExecuteNonQueryAsync());
        }

        public ValueTask DisposeAsync()
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            return ValueTask.CompletedTask;
        }
    }

    private sealed class OwnedConnectionDbContext(
        DbContextOptions<OwnedConnectionDbContext> options,
        SqliteConnection connection) : AppDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            foreach (var property in modelBuilder.Model.GetEntityTypes()
                         .SelectMany(entityType => entityType.GetProperties()).ToList())
            {
                var clrType = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
                var columnType = clrType switch
                {
                    _ when clrType == typeof(string) => "TEXT",
                    _ when clrType == typeof(byte[]) => "BLOB",
                    _ when clrType == typeof(float) || clrType == typeof(double) => "REAL",
                    _ when clrType == typeof(decimal) => "NUMERIC",
                    _ when clrType == typeof(DateTime) || clrType == typeof(DateTimeOffset) ||
                           clrType == typeof(TimeSpan) || clrType == typeof(Guid) => "TEXT",
                    _ => "INTEGER",
                };
                modelBuilder.Entity(property.DeclaringType.ClrType)
                    .Property(property.Name)
                    .HasColumnType(columnType);
            }
        }

        public override async ValueTask DisposeAsync()
        {
            await base.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    private sealed class BlockingTicketUpdateInterceptor : DbCommandInterceptor
    {
        private readonly TaskCompletionSource reached = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource release = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public Task UpdateReached => reached.Task;

        public void Release() => release.TrySetResult();

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("E_TICKET", StringComparison.OrdinalIgnoreCase) &&
                command.CommandText.Contains("UPDATE", StringComparison.OrdinalIgnoreCase))
            {
                reached.TrySetResult();
                await release.Task.WaitAsync(cancellationToken);
            }

            return result;
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
