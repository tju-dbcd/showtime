using System.Data;
using System.Data.Common;
using System.Text.RegularExpressions;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Oracle.ManagedDataAccess.Client;
using ShowtimeBackend.Common.TicketSecurity;
using ShowtimeBackend.Data;
using ShowtimeBackend.DTOs.OrderTicket;
using ShowtimeBackend.Entities.OrderTicket;
using ShowtimeBackend.Entities.SeatZone;
using ShowtimeBackend.Entities.ShowSession;
using ShowtimeBackend.Services.OrderTicket;
using Xunit;

namespace ShowtimeBackend.Tests.OrderTicket;

public sealed partial class OracleTicketRedemptionSafetyTests
{
    private const string SigningKey =
        "T1JBQ0xFUkVERU1QVElPTlRFU1RPTkxZS0VZMTIzNDU2Nzg=";

    [Theory]
    [InlineData("SELECT * FROM APP_OWNER.E_TICKET")]
    [InlineData("update app_owner.e_ticket set ticket_status = 'USED'")]
    [InlineData("SELECT * FROM \"APP_OWNER\".\"E_TICKET\"")]
    public void AppOwnerGuard_RejectsQualifiedSharedSchemaSql(string sql)
    {
        Assert.Throws<InvalidOperationException>(
            () => AppOwnerSqlGuardInterceptor.EnsureSafeCommandText(sql));
    }

    [Fact]
    public void AppOwnerGuard_AllowsValidatedPersonalSchemaSql()
    {
        AppOwnerSqlGuardInterceptor.EnsureSafeCommandText(
            "SELECT * FROM PERSONAL_TEST.E_TICKET");
    }

    [Theory]
    [InlineData("APP_OWNER")]
    [InlineData("deploy_user")]
    [InlineData("PERSONAL.TEST")]
    [InlineData("PERSONAL\" WHERE 1=1 --")]
    [InlineData("9PERSONAL")]
    public void PersonalSchemaValidation_RejectsSharedOrUnsafeIdentifiers(string schema)
    {
        Assert.Throws<InvalidOperationException>(
            () => PersonalOracleSchema.Validate(schema));
    }

    [OracleTicketRedemptionFact]
    public async Task OracleTicketRedemption_PersonalSchemaMappingPreflight()
    {
        var connectionString = Environment.GetEnvironmentVariable(
            "SHOWTIME_ORACLE_REDEMPTION_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "SHOWTIME_RUN_ORACLE_REDEMPTION_TESTS=1 requires " +
                "SHOWTIME_ORACLE_REDEMPTION_TEST_CONNECTION.");
        }

        var builder = new OracleConnectionStringBuilder(connectionString);
        var configuredSchema = PersonalOracleSchema.Validate(builder.UserID?.Trim());
        builder.ConnectionTimeout = 15;
        await using var connection = new OracleConnection(builder.ConnectionString);
        await connection.OpenAsync().WaitAsync(TimeSpan.FromSeconds(20));

        var sessionUser = PersonalOracleSchema.Validate(
            await ReadScalarAsync<string>(
                connection,
                "SELECT SYS_CONTEXT('USERENV', 'SESSION_USER') FROM DUAL"));
        var currentSchema = PersonalOracleSchema.Validate(
            await ReadScalarAsync<string>(
                connection,
                "SELECT SYS_CONTEXT('USERENV', 'CURRENT_SCHEMA') FROM DUAL"));
        if (!configuredSchema.Equals(sessionUser, StringComparison.OrdinalIgnoreCase) ||
            !configuredSchema.Equals(currentSchema, StringComparison.OrdinalIgnoreCase) ||
            sessionUser != sessionUser.ToUpperInvariant() ||
            currentSchema != currentSchema.ToUpperInvariant())
        {
            throw new InvalidOperationException(
                "Oracle redemption tests must log in as and remain in the same " +
                "unquoted personal schema.");
        }

        var requiredTableCount = await ReadScalarAsync<decimal>(
            connection,
            "SELECT COUNT(*) FROM USER_TABLES WHERE TABLE_NAME IN " +
            "('E_TICKET','ORDER_ITEM','T_ORDER','SHOW_SESSION','REFUND_REQUEST')");
        if (requiredTableCount != 5m)
        {
            throw new InvalidOperationException(
                "Oracle redemption tests require personal-schema base tables; " +
                "synonyms and shared-owner tables are refused.");
        }

        var triggerCount = await ReadScalarAsync<decimal>(
            connection,
            "SELECT COUNT(*) FROM USER_TRIGGERS " +
            "WHERE TRIGGER_NAME = 'TRG_ETICKET_UPDATE' AND STATUS = 'ENABLED'");
        if (triggerCount != 1m)
        {
            throw new InvalidOperationException(
                "The personal schema must own an enabled TRG_ETICKET_UPDATE trigger.");
        }

        var options = new DbContextOptionsBuilder<OracleRedemptionTestDbContext>()
            .UseOracle(connection)
            .ReplaceService<IModelCacheKeyFactory, PersonalSchemaModelCacheKeyFactory>()
            .AddInterceptors(new AppOwnerSqlGuardInterceptor())
            .Options;
        await using var db = new OracleRedemptionTestDbContext(
            options,
            currentSchema);
        var sql = db.Set<ETicket>()
            .AsNoTracking()
            .Where(ticket => ticket.ETicketId < 0)
            .ToQueryString();
        AppOwnerSqlGuardInterceptor.EnsureSafeCommandText(sql);
        Assert.Contains(currentSchema, sql, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await db.Set<ETicket>()
            .AsNoTracking()
            .Where(ticket => ticket.ETicketId < 0)
            .ToListAsync()
            .WaitAsync(TimeSpan.FromSeconds(20)));
    }

    [OracleTicketRedemptionFact]
    public async Task OracleTicketRedemption_DualConnectionAllowsExactlyOneWinner()
    {
        await using var fixture = await OracleRedemptionFixture.CreateAsync();
        var barrier = new TicketUpdateBarrier(expectedArrivals: 2);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(5), cancellation.Token);
            await using var firstDb = fixture.CreateContext(barrier);
            await using var secondDb = fixture.CreateContext(barrier);
            var firstTask = fixture.CreateRedemptionService(firstDb).RedeemAsync(
                "oracle-admin-1",
                new RedeemTicketRequest(fixture.Credential.QrCode, "oracle-gate-1"),
                cancellation.Token);
            var secondTask = fixture.CreateRedemptionService(secondDb).RedeemAsync(
                "oracle-admin-2",
                new RedeemTicketRequest(fixture.Credential.QrCode, "oracle-gate-2"),
                cancellation.Token);

            var results = await Task.WhenAll(firstTask, secondTask)
                .WaitAsync(TimeSpan.FromSeconds(40), cancellation.Token);

            var success = Assert.Single(results, result => result.IsSuccess);
            var loser = Assert.Single(results, result => !result.IsSuccess);
            Assert.Equal("TICKET_ALREADY_USED", loser.ErrorCode);
            Assert.Equal(DateTimeKind.Utc, success.Value!.CheckTime.Kind);
            Assert.Equal(0, success.Value.CheckTime.Ticks % 10);
            var json = JsonSerializer.Serialize(
                success.Value,
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            Assert.Matches("\\\"checkTime\\\":\\\"[^\\\"]+Z\\\"", json);

            await using var verificationDb = fixture.CreateContext();
            var ticket = await verificationDb.Set<ETicket>()
                .AsNoTracking()
                .SingleAsync(
                    item => item.ETicketId == fixture.ETicketId,
                    cancellation.Token);
            Assert.Equal("USED", ticket.TicketStatus);
            Assert.Equal(success.Value.CheckBy, ticket.CheckBy);
            Assert.Equal(success.Value.CheckDevice, ticket.CheckDevice);
            Assert.NotNull(ticket.CheckTime);
            Assert.True(ticket.UpdateTime > fixture.InitialTicketUpdateTime);
        }
        finally
        {
            barrier.Release();
        }
    }

    [OracleTicketRedemptionFact]
    public async Task OracleTicketRedemption_CompetingRefundCannotCreateCrossState()
    {
        await using var fixture = await OracleRedemptionFixture.CreateAsync();
        var barrier = new TicketUpdateBarrier(expectedArrivals: 2);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        try
        {
            await using var redemptionDb = fixture.CreateContext(barrier);
            await using var refundDb = fixture.CreateContext(barrier);
            var redemptionTask = fixture.CreateRedemptionService(redemptionDb).RedeemAsync(
                "oracle-admin",
                new RedeemTicketRequest(fixture.Credential.QrCode, "oracle-gate"),
                cancellation.Token);
            var refundTask = fixture.CreateRefundService(refundDb).CreateAsync(
                fixture.UserId,
                "oracle-user",
                fixture.OrderId,
                new CreateRefundRequest([fixture.OrderItemId], "Oracle race"),
                cancellation.Token);

            await Task.WhenAll(redemptionTask, refundTask)
                .WaitAsync(TimeSpan.FromSeconds(40), cancellation.Token);
            var redemption = await redemptionTask;
            var refund = await refundTask;
            Assert.NotEqual(redemption.IsSuccess, refund.IsSuccess);

            await using var verificationDb = fixture.CreateContext();
            var itemStatus = await verificationDb.Set<OrderItem>()
                .AsNoTracking()
                .Where(item => item.OrderItemId == fixture.OrderItemId)
                .Select(item => item.ItemStatus)
                .SingleAsync(cancellation.Token);
            var ticket = await verificationDb.Set<ETicket>()
                .AsNoTracking()
                .SingleAsync(
                    item => item.ETicketId == fixture.ETicketId,
                    cancellation.Token);
            var refundRequestCount = await verificationDb.Set<RefundRequest>()
                .AsNoTracking()
                .CountAsync(
                    item => item.OrderId == fixture.OrderId,
                    cancellation.Token);
            var refundItemCount = await verificationDb.Set<RefundItem>()
                .AsNoTracking()
                .CountAsync(
                    item => item.OrderItemId == fixture.OrderItemId,
                    cancellation.Token);

            if (redemption.IsSuccess)
            {
                Assert.False(refund.IsSuccess);
                Assert.Equal("NORMAL", itemStatus);
                Assert.Equal("USED", ticket.TicketStatus);
                Assert.NotNull(ticket.CheckTime);
                Assert.Equal(0, refundRequestCount);
                Assert.Equal(0, refundItemCount);
            }
            else
            {
                Assert.True(refund.IsSuccess);
                Assert.Equal("REFUNDING", itemStatus);
                Assert.Equal("REFUNDING", ticket.TicketStatus);
                Assert.Null(ticket.CheckTime);
                Assert.Null(ticket.CheckDevice);
                Assert.Null(ticket.CheckBy);
                Assert.Equal(1, refundRequestCount);
                Assert.Equal(1, refundItemCount);
            }
        }
        finally
        {
            barrier.Release();
        }
    }

    private static async Task<T> ReadScalarAsync<T>(
        OracleConnection connection,
        string commandText)
    {
        await using var command = connection.CreateCommand();
        command.CommandTimeout = 15;
        command.CommandText = commandText;
        var value = await command.ExecuteScalarAsync()
            .WaitAsync(TimeSpan.FromSeconds(20));
        if (value is null or DBNull)
        {
            throw new InvalidOperationException(
                $"Oracle preflight query returned no value: {commandText}");
        }

        return (T)Convert.ChangeType(value, typeof(T));
    }

    private sealed class OracleRedemptionFixture : IAsyncDisposable
    {
        private readonly string connectionString;
        private int disposed;

        private OracleRedemptionFixture(
            string connectionString,
            string schema,
            long userId,
            long showId,
            long seatId,
            long seatSectionId,
            long seatMapId,
            DateTimeOffset operationTime,
            ITicketTokenService tokenService,
            TicketCredential credential)
        {
            this.connectionString = connectionString;
            Schema = schema;
            UserId = userId;
            ShowId = showId;
            SeatId = seatId;
            SeatSectionId = seatSectionId;
            SeatMapId = seatMapId;
            OperationTime = operationTime;
            TokenService = tokenService;
            Credential = credential;
        }

        public string Schema { get; }
        public long UserId { get; }
        public long ShowId { get; }
        public long SeatId { get; }
        public long SeatSectionId { get; }
        public long SeatMapId { get; }
        public DateTimeOffset OperationTime { get; }
        public ITicketTokenService TokenService { get; }
        public TicketCredential Credential { get; }
        public long SessionId { get; private set; }
        public long PriceStrategyId { get; private set; }
        public long OrderId { get; private set; }
        public long OrderItemId { get; private set; }
        public long PaymentId { get; private set; }
        public long ETicketId { get; private set; }
        public long ReservationId { get; private set; }
        public long RefundPolicyId { get; private set; }
        public DateTime InitialTicketUpdateTime { get; private set; }

        public static async Task<OracleRedemptionFixture> CreateAsync()
        {
            var rawConnectionString = Environment.GetEnvironmentVariable(
                "SHOWTIME_ORACLE_REDEMPTION_TEST_CONNECTION");
            if (string.IsNullOrWhiteSpace(rawConnectionString))
            {
                throw new InvalidOperationException(
                    "SHOWTIME_RUN_ORACLE_REDEMPTION_TESTS=1 requires " +
                    "SHOWTIME_ORACLE_REDEMPTION_TEST_CONNECTION.");
            }

            var connectionBuilder = new OracleConnectionStringBuilder(
                rawConnectionString)
            {
                ConnectionTimeout = 15,
            };
            var configuredSchema = PersonalOracleSchema.Validate(
                connectionBuilder.UserID?.Trim());
            await using var connection = new OracleConnection(
                connectionBuilder.ConnectionString);
            await connection.OpenAsync().WaitAsync(TimeSpan.FromSeconds(20));
            var schema = await ValidateConnectionAsync(
                connection,
                configuredSchema);
            await EnsureRequiredObjectsAsync(connection);
            var dependencies = await ReadDependenciesAsync(connection);

            var operationTime = TruncateToMicroseconds(DateTimeOffset.UtcNow);
            var tokenService = new HmacTicketTokenService(
                Options.Create(new TicketSecurityOptions
                {
                    SigningKeyBase64 = SigningKey,
                }));
            var fixture = new OracleRedemptionFixture(
                connectionBuilder.ConnectionString,
                schema,
                dependencies.UserId,
                dependencies.ShowId,
                dependencies.SeatId,
                dependencies.SeatSectionId,
                dependencies.SeatMapId,
                operationTime,
                tokenService,
                tokenService.Generate(operationTime));
            try
            {
                await fixture.SeedAsync();
                return fixture;
            }
            catch
            {
                await fixture.DisposeAsync();
                throw;
            }
        }

        public OracleRedemptionTestDbContext CreateContext(
            params IInterceptor[] interceptors)
        {
            var options = new DbContextOptionsBuilder<OracleRedemptionTestDbContext>()
                .UseOracle(
                    connectionString,
                    oracle => oracle.CommandTimeout(15))
                .ReplaceService<IModelCacheKeyFactory, PersonalSchemaModelCacheKeyFactory>()
                .AddInterceptors(
                    new IInterceptor[] { new AppOwnerSqlGuardInterceptor() }
                        .Concat(interceptors))
                .Options;
            return new OracleRedemptionTestDbContext(options, Schema);
        }

        public TicketRedemptionService CreateRedemptionService(AppDbContext db) => new(
            db,
            TokenService,
            new FixedTimeProvider(OperationTime),
            Options.Create(new TicketRedemptionOptions
            {
                OpenBeforeMinutes = 10080,
                CloseAfterMinutes = 120,
            }),
            NullLogger<TicketRedemptionService>.Instance,
            new NullOrderTicketAuditSink());

        public RefundApplicationService CreateRefundService(AppDbContext db) => new(
            db,
            new RefundPolicyEngine(),
            new FixedTimeProvider(OperationTime),
            new PersonalSchemaRefundLockCoordinator(db, Schema),
            NullLogger<RefundApplicationService>.Instance,
            new NullOrderTicketAuditSink());

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0)
            {
                return;
            }

            await using var connection = new OracleConnection(connectionString);
            await connection.OpenAsync().WaitAsync(TimeSpan.FromSeconds(20));
            await using var transaction = await connection.BeginTransactionAsync();
            try
            {
                await ExecuteDeleteAsync(
                    connection,
                    transaction,
                    $"DELETE FROM {Schema}.REFUND_ITEM WHERE ORDER_ITEM_ID = :id",
                    OrderItemId);
                await ExecuteDeleteAsync(
                    connection,
                    transaction,
                    $"DELETE FROM {Schema}.REFUND_REQUEST WHERE ORDER_ID = :id",
                    OrderId);
                await ExecuteDeleteAsync(
                    connection,
                    transaction,
                    $"DELETE FROM {Schema}.E_TICKET WHERE ETICKET_ID = :id",
                    ETicketId);
                await ExecuteDeleteAsync(
                    connection,
                    transaction,
                    $"DELETE FROM {Schema}.SEAT_RESERVATION " +
                    "WHERE SEAT_RESERVATION_ID = :id",
                    ReservationId);
                await ExecuteDeleteAsync(
                    connection,
                    transaction,
                    $"DELETE FROM {Schema}.PAYMENT WHERE PAYMENT_ID = :id",
                    PaymentId);
                await ExecuteDeleteAsync(
                    connection,
                    transaction,
                    $"DELETE FROM {Schema}.ORDER_ITEM WHERE ORDER_ITEM_ID = :id",
                    OrderItemId);
                await ExecuteDeleteAsync(
                    connection,
                    transaction,
                    $"DELETE FROM {Schema}.T_ORDER WHERE ORDER_ID = :id",
                    OrderId);
                await ExecuteDeleteAsync(
                    connection,
                    transaction,
                    $"DELETE FROM {Schema}.PRICE_STRATEGY " +
                    "WHERE PRICE_STRATEGY_ID = :id",
                    PriceStrategyId);
                await ExecuteDeleteAsync(
                    connection,
                    transaction,
                    $"DELETE FROM {Schema}.REFUND_POLICY WHERE POLICY_ID = :id",
                    RefundPolicyId);
                await ExecuteDeleteAsync(
                    connection,
                    transaction,
                    $"DELETE FROM {Schema}.SHOW_SESSION WHERE SESSION_ID = :id",
                    SessionId);
                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        private async Task SeedAsync()
        {
            await using var db = CreateContext();
            var operationTime = OperationTime.UtcDateTime;
            var session = new ShowSession
            {
                ShowId = ShowId,
                SeatMapId = SeatMapId,
                StartTime = operationTime.AddHours(168),
                EndTime = operationTime.AddHours(170),
                SaleStartTime = operationTime.AddDays(-30),
                SaleEndTime = operationTime.AddDays(-1),
                SessionStatus = "ENDED",
                CreateBy = "oracle-test",
                UpdateBy = "oracle-test",
            };
            db.Add(session);
            await db.SaveChangesAsync();
            SessionId = session.SessionId;

            var strategy = new PriceStrategy
            {
                SessionId = SessionId,
                SeatSectionId = SeatSectionId,
                StrategyName = $"Oracle redemption {Guid.NewGuid():N}"[..50],
                PriceType = "STANDARD",
                Price = 188m,
                SaleStartTime = operationTime.AddDays(-30),
                SaleEndTime = operationTime.AddDays(-1),
                Priority = 1,
                Status = "ENABLED",
                CreateBy = "oracle-test",
                UpdateBy = "oracle-test",
            };
            db.Add(strategy);
            await db.SaveChangesAsync();
            PriceStrategyId = strategy.PriceStrategyId;

            var unique = Guid.NewGuid().ToString("N").ToUpperInvariant();
            var order = new Order
            {
                OrderNo = $"ORT{unique}"[..30],
                UserId = UserId,
                SessionId = SessionId,
                TotalAmount = 188m,
                DiscountAmount = 0m,
                TicketCount = 1,
                OrderStatus = "ISSUED",
                ExpireTime = operationTime.AddDays(-1),
                PayTime = operationTime.AddDays(-1),
                IssueTime = operationTime.AddDays(-1),
                Source = "WEB",
                CreateBy = "oracle-test",
                UpdateBy = "oracle-test",
            };
            db.Add(order);
            await db.SaveChangesAsync();
            OrderId = order.OrderId;

            var orderItem = new OrderItem
            {
                OrderId = OrderId,
                SeatId = SeatId,
                PriceStrategyId = PriceStrategyId,
                UnitPrice = 188m,
                ItemStatus = "NORMAL",
                CreateBy = "oracle-test",
                UpdateBy = "oracle-test",
            };
            db.Add(orderItem);
            await db.SaveChangesAsync();
            OrderItemId = orderItem.OrderItemId;

            var payment = new Payment
            {
                PaymentNo = $"ORP{unique}"[..35],
                OrderId = OrderId,
                UserId = UserId,
                PayAmount = 188m,
                PayChannel = "ALIPAY",
                PayStatus = "SUCCESS",
                PayTime = operationTime.AddDays(-1),
                RefundAmount = 0m,
                CreateBy = "oracle-test",
                UpdateBy = "oracle-test",
            };
            var ticket = new ETicket
            {
                ETicketNo = Credential.TicketNo,
                OrderItemId = OrderItemId,
                UserId = UserId,
                QrCode = Credential.QrCode,
                AntiFakeCode = Credential.AntiFakeCode,
                TicketStatus = "UNUSED",
                CreateBy = "oracle-test",
                UpdateBy = "oracle-test",
            };
            var reservation = new SeatReservation
            {
                SessionId = SessionId,
                SeatId = SeatId,
                OrderItemId = OrderItemId,
                ReservationType = "ORDER",
                ReservationStatus = "ACTIVE",
                CreateBy = "oracle-test",
                UpdateBy = "oracle-test",
            };
            var refundPolicy = new RefundPolicy
            {
                ShowId = ShowId,
                PolicyName = $"Oracle redemption {unique}"[..50],
                RefundDeadlineHour = 168,
                RefundRate = 1m,
                ServiceFee = 0m,
                Priority = 1,
                Status = 1,
                CreateBy = "oracle-test",
                UpdateBy = "oracle-test",
            };
            db.AddRange(payment, ticket, reservation, refundPolicy);
            await db.SaveChangesAsync();
            PaymentId = payment.PaymentId;
            ETicketId = ticket.ETicketId;
            ReservationId = reservation.SeatReservationId;
            RefundPolicyId = refundPolicy.PolicyId;
            await db.Entry(ticket).ReloadAsync();
            InitialTicketUpdateTime = ticket.UpdateTime;
        }

        private static async Task<string> ValidateConnectionAsync(
            OracleConnection connection,
            string configuredSchema)
        {
            var sessionUser = PersonalOracleSchema.Validate(
                await ReadScalarAsync<string>(
                    connection,
                    "SELECT SYS_CONTEXT('USERENV', 'SESSION_USER') FROM DUAL"));
            var currentSchema = PersonalOracleSchema.Validate(
                await ReadScalarAsync<string>(
                    connection,
                    "SELECT SYS_CONTEXT('USERENV', 'CURRENT_SCHEMA') FROM DUAL"));
            if (!configuredSchema.Equals(sessionUser, StringComparison.OrdinalIgnoreCase) ||
                !configuredSchema.Equals(currentSchema, StringComparison.OrdinalIgnoreCase) ||
                sessionUser != sessionUser.ToUpperInvariant() ||
                currentSchema != currentSchema.ToUpperInvariant())
            {
                throw new InvalidOperationException(
                    "Oracle redemption tests must log in as and remain in the " +
                    "same unquoted personal schema.");
            }

            return currentSchema;
        }

        private static async Task EnsureRequiredObjectsAsync(
            OracleConnection connection)
        {
            const string requiredTables =
                "'SYS_USER','SHOW','SEAT_MAP','SEAT_SECTION','SEAT'," +
                "'SHOW_SESSION','PRICE_STRATEGY','T_ORDER','ORDER_ITEM'," +
                "'PAYMENT','E_TICKET','SEAT_RESERVATION','REFUND_POLICY'," +
                "'REFUND_REQUEST','REFUND_ITEM','EXCHANGE_ITEM'";
            var requiredTableCount = await ReadScalarAsync<decimal>(
                connection,
                $"SELECT COUNT(*) FROM USER_TABLES WHERE TABLE_NAME IN ({requiredTables})");
            if (requiredTableCount != 16m)
            {
                throw new InvalidOperationException(
                    "Oracle redemption competition tests require all base tables " +
                    "to be owned by the personal schema; synonyms are refused.");
            }

            var triggerCount = await ReadScalarAsync<decimal>(
                connection,
                "SELECT COUNT(*) FROM USER_TRIGGERS " +
                "WHERE TRIGGER_NAME = 'TRG_ETICKET_UPDATE' AND STATUS = 'ENABLED'");
            if (triggerCount != 1m)
            {
                throw new InvalidOperationException(
                    "The personal schema must own an enabled " +
                    "TRG_ETICKET_UPDATE trigger.");
            }

            var removedSessionColumnCount = await ReadScalarAsync<decimal>(
                connection,
                "SELECT COUNT(*) FROM USER_TAB_COLUMNS " +
                "WHERE TABLE_NAME = 'E_TICKET' AND COLUMN_NAME = 'SESSION_ID'");
            if (removedSessionColumnCount != 0m)
            {
                throw new InvalidOperationException(
                    "The personal schema must include the applied order-ticket " +
                    "migration that removes E_TICKET.SESSION_ID.");
            }
        }

        private static async Task<FixtureDependencies> ReadDependenciesAsync(
            OracleConnection connection)
        {
            await using var command = connection.CreateCommand();
            command.CommandTimeout = 15;
            command.CommandText =
                "SELECT U.USER_ID, S.SHOW_ID, X.SEAT_ID, " +
                "X.SEAT_SECTION_ID, X.SEAT_MAP_ID " +
                "FROM (SELECT USER_ID FROM SYS_USER WHERE ROWNUM = 1) U " +
                "CROSS JOIN (SELECT SHOW_ID FROM SHOW SH " +
                "WHERE NOT EXISTS (SELECT 1 FROM REFUND_POLICY RP " +
                "WHERE RP.SHOW_ID = SH.SHOW_ID AND RP.STATUS = 1 " +
                "AND RP.REFUND_DEADLINE_HOUR = 168) AND ROWNUM = 1) S " +
                "CROSS JOIN (SELECT ST.SEAT_ID, SS.SEAT_SECTION_ID, " +
                "SS.SEAT_MAP_ID FROM SEAT ST JOIN SEAT_SECTION SS " +
                "ON SS.SEAT_SECTION_ID = ST.SEAT_SECTION_ID " +
                "WHERE ROWNUM = 1) X";
            await using var reader = await command.ExecuteReaderAsync()
                .WaitAsync(TimeSpan.FromSeconds(20));
            if (!await reader.ReadAsync().WaitAsync(TimeSpan.FromSeconds(20)))
            {
                throw new InvalidOperationException(
                    "The personal Oracle schema needs a user, a show without a " +
                    "conflicting 168-hour policy, and a seat for isolated fixtures.");
            }

            return new FixtureDependencies(
                Convert.ToInt64(reader.GetValue(0)),
                Convert.ToInt64(reader.GetValue(1)),
                Convert.ToInt64(reader.GetValue(2)),
                Convert.ToInt64(reader.GetValue(3)),
                Convert.ToInt64(reader.GetValue(4)));
        }

        private static async Task ExecuteDeleteAsync(
            OracleConnection connection,
            DbTransaction transaction,
            string commandText,
            long id)
        {
            if (id <= 0)
            {
                return;
            }

            AppOwnerSqlGuardInterceptor.EnsureSafeCommandText(commandText);
            await using var command = connection.CreateCommand();
            command.BindByName = true;
            command.CommandTimeout = 15;
            command.Transaction = (OracleTransaction)transaction;
            command.CommandText = commandText;
            command.Parameters.Add(
                new OracleParameter(
                    "id",
                    OracleDbType.Int64,
                    id,
                    ParameterDirection.Input));
            await command.ExecuteNonQueryAsync().WaitAsync(TimeSpan.FromSeconds(20));
        }

        private static DateTimeOffset TruncateToMicroseconds(DateTimeOffset value)
        {
            var utc = value.ToUniversalTime();
            return new DateTimeOffset(
                utc.Ticks - utc.Ticks % 10,
                TimeSpan.Zero);
        }

        private sealed record FixtureDependencies(
            long UserId,
            long ShowId,
            long SeatId,
            long SeatSectionId,
            long SeatMapId);
    }

    private sealed class TicketUpdateBarrier(int expectedArrivals)
        : DbCommandInterceptor
    {
        private readonly TaskCompletionSource<bool> release = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private int arrivals;

        public void Release() => release.TrySetResult(true);

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            await WaitAtTicketUpdateAsync(command, cancellationToken);
            return result;
        }

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            await WaitAtTicketUpdateAsync(command, cancellationToken);
            return result;
        }

        private async Task WaitAtTicketUpdateAsync(
            DbCommand command,
            CancellationToken cancellationToken)
        {
            if (!command.CommandText.Contains(
                    "UPDATE",
                    StringComparison.OrdinalIgnoreCase) ||
                !command.CommandText.Contains(
                    "E_TICKET",
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (Interlocked.Increment(ref arrivals) >= expectedArrivals)
            {
                release.TrySetResult(true);
            }

            await release.Task.WaitAsync(
                TimeSpan.FromSeconds(15),
                cancellationToken);
        }
    }

    private sealed class PersonalSchemaRefundLockCoordinator(
        AppDbContext db,
        string schema) : IRefundLockCoordinator
    {
        private readonly string safeSchema = PersonalOracleSchema.Validate(schema);

        public Task<bool> LockRefundRequestAsync(
            long refundId,
            CancellationToken cancellationToken) => LockAsync(
            "REFUND_REQUEST",
            "REFUND_ID",
            refundId,
            cancellationToken);

        public Task<bool> LockOrderAsync(
            long orderId,
            CancellationToken cancellationToken) => LockAsync(
            "T_ORDER",
            "ORDER_ID",
            orderId,
            cancellationToken);

        private async Task<bool> LockAsync(
            string table,
            string column,
            long id,
            CancellationToken cancellationToken)
        {
            var transaction = db.Database.CurrentTransaction ??
                throw new InvalidOperationException(
                    "An Oracle refund lock requires an active transaction.");
            var sql =
                $"SELECT {column} FROM {safeSchema}.{table} " +
                $"WHERE {column} = :id FOR UPDATE";
            AppOwnerSqlGuardInterceptor.EnsureSafeCommandText(sql);
            var connection = db.Database.GetDbConnection();
            await using var command = connection.CreateCommand();
            command.CommandTimeout = 15;
            command.CommandText = sql;
            command.Transaction = transaction.GetDbTransaction();
            var parameter = command.CreateParameter();
            parameter.ParameterName = "id";
            parameter.DbType = DbType.Int64;
            parameter.Value = id;
            command.Parameters.Add(parameter);
            var result = await command.ExecuteScalarAsync(cancellationToken);
            return result is not null and not DBNull;
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class OracleTicketRedemptionFactAttribute : FactAttribute
    {
        public OracleTicketRedemptionFactAttribute()
        {
            if (!string.Equals(
                    Environment.GetEnvironmentVariable(
                        "SHOWTIME_RUN_ORACLE_REDEMPTION_TESTS"),
                    "1",
                    StringComparison.Ordinal))
            {
                Skip =
                    "SHOWTIME_RUN_ORACLE_REDEMPTION_TESTS is not 1; " +
                    "no Oracle connection will be opened.";
            }
        }
    }

    private sealed class OracleRedemptionTestDbContext(
        DbContextOptions<OracleRedemptionTestDbContext> options,
        string schema) : AppDbContext(options)
    {
        public string PersonalSchema { get; } = PersonalOracleSchema.Validate(schema);

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.HasDefaultSchema(PersonalSchema);
        }
    }

    private sealed class PersonalSchemaModelCacheKeyFactory : IModelCacheKeyFactory
    {
        public object Create(DbContext context, bool designTime) => context is
            OracleRedemptionTestDbContext personal
                ? (context.GetType(), personal.PersonalSchema, designTime)
                : (object)(context.GetType(), designTime);
    }

    private sealed partial class AppOwnerSqlGuardInterceptor : DbCommandInterceptor
    {
        public static void EnsureSafeCommandText(string commandText)
        {
            if (AppOwnerQualifier().IsMatch(commandText))
            {
                throw new InvalidOperationException(
                    "Oracle redemption tests refuse every APP_OWNER-qualified command.");
            }
        }

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result)
        {
            EnsureSafeCommandText(command.CommandText);
            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            EnsureSafeCommandText(command.CommandText);
            return ValueTask.FromResult(result);
        }

        public override InterceptionResult<int> NonQueryExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result)
        {
            EnsureSafeCommandText(command.CommandText);
            return result;
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            EnsureSafeCommandText(command.CommandText);
            return ValueTask.FromResult(result);
        }

        public override InterceptionResult<object> ScalarExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<object> result)
        {
            EnsureSafeCommandText(command.CommandText);
            return result;
        }

        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<object> result,
            CancellationToken cancellationToken = default)
        {
            EnsureSafeCommandText(command.CommandText);
            return ValueTask.FromResult(result);
        }

        [GeneratedRegex(
            "(?:\\\"APP_OWNER\\\"|APP_OWNER)\\s*\\.",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
        private static partial Regex AppOwnerQualifier();
    }

    private static class PersonalOracleSchema
    {
        public static string Validate(string? identifier)
        {
            if (string.IsNullOrWhiteSpace(identifier) ||
                identifier.Length > 128 ||
                !IsAsciiLetter(identifier[0]) ||
                identifier.Any(character =>
                    !IsAsciiLetter(character) &&
                    !char.IsAsciiDigit(character) &&
                    character is not ('_' or '$' or '#')) ||
                identifier.Equals("APP_OWNER", StringComparison.OrdinalIgnoreCase) ||
                identifier.Equals("DEPLOY_USER", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Oracle redemption tests require a safe personal schema and " +
                    "refuse APP_OWNER or DEPLOY_USER.");
            }

            return identifier;
        }

        private static bool IsAsciiLetter(char character) =>
            character is >= 'A' and <= 'Z' or >= 'a' and <= 'z';
    }
}
