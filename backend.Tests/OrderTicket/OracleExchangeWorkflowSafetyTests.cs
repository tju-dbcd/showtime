using System.Data;
using System.Data.Common;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Options;
using Oracle.ManagedDataAccess.Client;
using ShowtimeBackend.Common;
using ShowtimeBackend.Common.TicketSecurity;
using ShowtimeBackend.Data;
using ShowtimeBackend.DTOs.OrderTicket;
using ShowtimeBackend.Entities.OrderTicket;
using ShowtimeBackend.Entities.SeatZone;
using ShowtimeBackend.Entities.ShowSession;
using ShowtimeBackend.Services.OrderTicket;

namespace ShowtimeBackend.Tests.OrderTicket;

public sealed class OracleExchangeWorkflowSafetyTests
{
    private const long SessionId = 998_810_001;
    private const long OrderId = 998_810_001;
    private const long ExchangeId = 998_810_001;
    private const long PolicyId = 998_810_001;

    [OracleExchangeFact]
    public async Task OracleExchangeMigration_SameScriptIsIdempotentAndRepairsMissingIndex()
    {
        using var oracleGate = await OracleOrderTicketGate.EnterAsync();
        await using var connection = await OpenValidatedConnectionAsync();

        await ExecuteMigrationAsync(connection);
        await ExecuteMigrationAsync(connection);
        try
        {
            await ExecuteAsync(connection, "DROP INDEX IDX_EXCHANGE_APPLIED_POLICY");
            Assert.Equal(0m, await ScalarAsync<decimal>(connection,
                "SELECT COUNT(*) FROM USER_INDEXES " +
                "WHERE INDEX_NAME = 'IDX_EXCHANGE_APPLIED_POLICY'"));

            await ExecuteMigrationAsync(connection);

            Assert.Equal(1m, await ScalarAsync<decimal>(connection,
                "SELECT COUNT(*) FROM USER_INDEXES " +
                "WHERE INDEX_NAME = 'IDX_EXCHANGE_APPLIED_POLICY' " +
                "AND UNIQUENESS = 'NONUNIQUE'"));
        }
        finally
        {
            await ExecuteMigrationAsync(connection);
        }
    }

    [OracleExchangeTheory]
    [InlineData("ticket-temp-constraint")]
    [InlineData("policy-link-chain")]
    [InlineData("state-constraint")]
    [InlineData("item-index")]
    [InlineData("legacy-item-unique")]
    [InlineData("policy-precision")]
    public async Task OracleExchangeMigration_RepairsEverySupportedInterruptionBoundary(
        string boundary)
    {
        using var oracleGate = await OracleOrderTicketGate.EnterAsync();
        await using var connection = await OpenValidatedConnectionAsync();
        await ExecuteMigrationAsync(connection);
        try
        {
            await BreakMigrationBoundaryAsync(connection, boundary);
            await ExecuteMigrationAsync(connection);
            await AssertMigrationTerminalStateAsync(connection);
        }
        finally
        {
            await ExecuteMigrationAsync(connection);
        }
    }

    [OracleExchangeFact]
    public async Task OracleExchangeMigration_FinalStateExistsInPersonalSchema()
    {
        using var oracleGate = await OracleOrderTicketGate.EnterAsync();
        await using var connection = await OpenValidatedConnectionAsync();
        await ExecuteMigrationAsync(connection);

        await AssertMigrationTerminalStateAsync(connection);
    }

    [OracleExchangeFact]
    public async Task OracleExchangeLockCoordinator_UsesPersonalSchemaAndSerializesRootOrder()
    {
        using var oracleGate = await OracleOrderTicketGate.EnterAsync();
        await using var firstConnection = await OpenValidatedConnectionAsync();
        await ExecuteMigrationAsync(firstConnection);
        var schema = await ScalarAsync<string>(
            firstConnection,
            "SELECT SYS_CONTEXT('USERENV', 'CURRENT_SCHEMA') FROM DUAL");
        await SeedRootOrderAsync(firstConnection);
        try
        {
            var options = new DbContextOptionsBuilder<OracleExchangeTestDbContext>()
                .UseOracle(
                    firstConnection,
                    oracle =>
                    {
                        oracle.CommandTimeout(15);
                        oracle.UseOracleSQLCompatibility(
                            OracleSQLCompatibility.DatabaseVersion21);
                    })
                .ReplaceService<IModelCacheKeyFactory, PersonalSchemaModelCacheKeyFactory>()
                .AddInterceptors(new AppOwnerSqlGuardInterceptor())
                .Options;
            await using var db = new OracleExchangeTestDbContext(options, schema);
            await using var transaction = await db.Database.BeginTransactionAsync();
            var coordinator = new OracleExchangeLockCoordinator(db);

            Assert.True(await coordinator.LockOrderAsync(OrderId, CancellationToken.None));

            await using var secondConnection = await OpenValidatedConnectionAsync();
            await using var competing = secondConnection.CreateCommand();
            competing.BindByName = true;
            competing.CommandText =
                $"SELECT ORDER_ID FROM {schema}.T_ORDER " +
                "WHERE ORDER_ID = :id FOR UPDATE NOWAIT";
            competing.Parameters.Add(
                new OracleParameter(
                    "id",
                    OracleDbType.Int64,
                    OrderId,
                    ParameterDirection.Input));
            var exception = await Assert.ThrowsAsync<OracleException>(
                () => competing.ExecuteScalarAsync());
            Assert.Equal(54, exception.Number);

            await transaction.RollbackAsync();
        }
        finally
        {
            await CleanupRootOrderAsync(firstConnection);
        }
    }

    [OracleExchangeFact]
    public async Task OracleExchangeStateAndPolicy_PersistDocumentedDomainValues()
    {
        using var oracleGate = await OracleOrderTicketGate.EnterAsync();
        await using var connection = await OpenValidatedConnectionAsync();
        await ExecuteMigrationAsync(connection);
        await SeedRootOrderAsync(connection);
        try
        {
            var userId = await ScalarAsync<decimal>(
                connection,
                "SELECT USER_ID FROM T_ORDER WHERE ORDER_ID = 998810001");
            await ExecuteAsync(
                connection,
                "INSERT INTO EXCHANGE_POLICY (" +
                "POLICY_ID, POLICY_NAME, EXCHANGE_DEADLINE_HOUR, EXCHANGE_FEE, " +
                "ALLOW_CROSS_SESSION, PRIORITY, STATUS, CREATE_BY, UPDATE_BY) " +
                "VALUES (:id, 'oracle-exchange-policy-gate', 24, 1.25, 0, 1, 1, " +
                "'oracle-exchange-gate', 'oracle-exchange-gate')",
                ("id", OracleDbType.Int64, (object)PolicyId));
            Assert.Equal(0m, await ScalarAsync<decimal>(connection,
                "SELECT ALLOW_CROSS_SESSION FROM EXCHANGE_POLICY " +
                "WHERE POLICY_ID = 998810001"));
            await ExecuteAsync(
                connection,
                "UPDATE EXCHANGE_POLICY SET ALLOW_CROSS_SESSION = 1, STATUS = 0 " +
                "WHERE POLICY_ID = :id",
                ("id", OracleDbType.Int64, (object)PolicyId));
            Assert.Equal(1m, await ScalarAsync<decimal>(connection,
                "SELECT ALLOW_CROSS_SESSION FROM EXCHANGE_POLICY " +
                "WHERE POLICY_ID = 998810001"));
            Assert.Equal(0m, await ScalarAsync<decimal>(connection,
                "SELECT STATUS FROM EXCHANGE_POLICY WHERE POLICY_ID = 998810001"));

            await ExecuteAsync(
                connection,
                "INSERT INTO EXCHANGE_REQUEST (" +
                "EXCHANGE_ID, EXCHANGE_NO, ORDER_ID, USER_ID, ORIG_SESSION_ID, " +
                "TARGET_SESSION_ID, EXCHANGE_FEE, PRICE_DIFF, APPLIED_POLICY_ID, " +
                "APPROVE_STATUS, EXCHANGE_STATUS, CREATE_BY, UPDATE_BY) VALUES (" +
                ":exchangeId, 'EXGATE998810001', :orderId, :userId, " +
                ":sessionId, :sessionId, 1.25, 2.00, :policyId, 'PENDING', " +
                "'PENDING', 'oracle-exchange-gate', 'oracle-exchange-gate')",
                ("exchangeId", OracleDbType.Int64, (object)ExchangeId),
                ("orderId", OracleDbType.Int64, (object)OrderId),
                ("userId", OracleDbType.Int64, userId),
                ("sessionId", OracleDbType.Int64, (object)SessionId),
                ("policyId", OracleDbType.Int64, (object)PolicyId));
            await ExecuteAsync(
                connection,
                "UPDATE EXCHANGE_REQUEST SET APPROVE_STATUS = 'APPROVED', " +
                "EXCHANGE_STATUS = 'PROCESSING' WHERE EXCHANGE_ID = :id",
                ("id", OracleDbType.Int64, (object)ExchangeId));
            var invalid = await Assert.ThrowsAsync<OracleException>(() => ExecuteAsync(
                connection,
                "UPDATE EXCHANGE_REQUEST SET EXCHANGE_STATUS = 'PENDING' " +
                "WHERE EXCHANGE_ID = :id",
                ("id", OracleDbType.Int64, (object)ExchangeId)));
            Assert.Equal(2290, invalid.Number);
            await ExecuteAsync(connection, "ROLLBACK");
        }
        finally
        {
            await CleanupRootOrderAsync(connection);
        }
    }

    [OracleExchangeFact]
    public async Task OracleExchangeWorkflow_CreateApproveFailThenPay_ReconcilesTerminalAggregate()
    {
        using var oracleGate = await OracleOrderTicketGate.EnterAsync();
        await EnsureMigrationAsync();
        await using var fixture = await OracleWorkflowFixture.CreateAsync(targetPrice: 125m);

        var created = await fixture.Application.CreateAsync(
            fixture.UserId,
            "oracle-user",
            OracleWorkflowFixture.OriginalOrderId,
            fixture.CreateRequest);
        Assert.True(created.IsSuccess, created.Message);
        var approved = await fixture.Review.ApproveAsync(
            "oracle-admin",
            created.Value!.ExchangeId,
            new ApproveExchangeRequest(null));
        Assert.True(approved.IsSuccess, approved.Message);
        Assert.Equal(ExchangeStatus.PROCESSING, approved.Value!.ExchangeStatus);

        var failed = await fixture.Payment.PayAsync(
            fixture.UserId,
            "oracle-user",
            created.Value.ExchangeId,
            new ExchangePaymentRequest(PaymentChannel.ALIPAY, PaymentResult.FAIL));
        Assert.True(failed.IsSuccess, failed.Message);
        Assert.Equal(PaymentStatus.FAIL, failed.Value!.Payment.PayStatus);
        var completed = await fixture.Payment.PayAsync(
            fixture.UserId,
            "oracle-user",
            created.Value.ExchangeId,
            new ExchangePaymentRequest(PaymentChannel.WECHAT, PaymentResult.SUCCESS));
        Assert.True(completed.IsSuccess, completed.Message);
        Assert.Equal(ExchangeStatus.COMPLETED, completed.Value!.Exchange.ExchangeStatus);
        Assert.Equal(2, await fixture.Db.Set<Payment>()
            .CountAsync(item => item.OrderId == completed.Value.Exchange.ChildOrderId));
        Assert.Equal("EXCHANGED", await fixture.Db.Set<ETicket>().AsNoTracking()
            .Where(item => item.OrderItemId == OracleWorkflowFixture.OriginalItemId)
            .Select(item => item.TicketStatus).SingleAsync());
        Assert.Equal("UNUSED", completed.Value.Exchange.Items.Single().NewTicketStatus!.Value.ToString());
    }

    [OracleExchangeFact]
    public async Task OracleExchangeWorkflow_TwoConnectionsCompetingForOriginalTicket_OneWins()
    {
        using var oracleGate = await OracleOrderTicketGate.EnterAsync();
        await EnsureMigrationAsync();
        await using var fixture = await OracleWorkflowFixture.CreateAsync(targetPrice: 125m);
        await using var secondConnection = await OpenValidatedConnectionAsync();
        var schema = await ScalarAsync<string>(secondConnection,
            "SELECT SYS_CONTEXT('USERENV', 'CURRENT_SCHEMA') FROM DUAL");
        var secondOptions = new DbContextOptionsBuilder<OracleExchangeTestDbContext>()
            .UseOracle(secondConnection, oracle =>
            {
                oracle.CommandTimeout(30);
                oracle.UseOracleSQLCompatibility(
                    OracleSQLCompatibility.DatabaseVersion21);
            })
            .ReplaceService<IModelCacheKeyFactory, PersonalSchemaModelCacheKeyFactory>()
            .AddInterceptors(new AppOwnerSqlGuardInterceptor())
            .Options;
        await using var secondDb = new OracleExchangeTestDbContext(secondOptions, schema);
        var secondApplication = new ExchangeApplicationService(
            secondDb,
            new ExchangePolicyEngine(),
            fixture.TimeProvider,
            new OracleExchangeLockCoordinator(secondDb),
            Options.Create(new ExchangeOptions()));
        var start = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var arrivals = 0;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));

        var results = await Task.WhenAll(
            RunAsync(fixture.Application, "oracle-racer-1"),
            RunAsync(secondApplication, "oracle-racer-2"));

        Assert.Single(results, result => result.IsSuccess);
        var loser = Assert.Single(results, result => !result.IsSuccess);
        Assert.Contains(
            loser.ErrorCode,
            new[] { "EXCHANGE_ACTIVE_REQUEST_EXISTS", "EXCHANGE_ITEM_NOT_ELIGIBLE" });
        fixture.Db.ChangeTracker.Clear();
        Assert.Equal(1, await fixture.Db.Set<ExchangeRequest>().AsNoTracking()
            .CountAsync(item => item.OrderId == OracleWorkflowFixture.OriginalOrderId));

        async Task<OrderTicketResult<ExchangeResponse>> RunAsync(
            ExchangeApplicationService service,
            string actor)
        {
            if (Interlocked.Increment(ref arrivals) == 2)
                start.TrySetResult();
            await start.Task.WaitAsync(timeout.Token);
            return await service.CreateAsync(
                fixture.UserId,
                actor,
                OracleWorkflowFixture.OriginalOrderId,
                fixture.CreateRequest,
                timeout.Token);
        }
    }

    [OracleExchangeFact]
    public async Task OracleExchangeWorkflow_RedeemVersusExchange_OnlyOneOwnsOriginalTicket()
    {
        using var oracleGate = await OracleOrderTicketGate.EnterAsync();
        await EnsureMigrationAsync();
        await using var fixture = await OracleWorkflowFixture.CreateAsync(targetPrice: 125m);
        var (secondConnection, secondDb) = await OpenWorkflowDbAsync();
        await using var connectionLease = secondConnection;
        await using var dbLease = secondDb;
        var redemption = new TicketRedemptionService(
            secondDb,
            new ExistingOracleTicketTokenService(
                "EXGATE-TKT-998820001", "exgate-original-qr"),
            fixture.TimeProvider,
            Options.Create(new TicketRedemptionOptions
            {
                OpenBeforeMinutes = 10_080,
                CloseAfterMinutes = 120,
            }),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<TicketRedemptionService>.Instance,
            new NullOrderTicketAuditSink());
        var barrier = new OracleTwoPartyBarrier();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));

        var results = await Task.WhenAll(RunExchangeAsync(), RunRedeemAsync());

        Assert.Single(results, result => result);
        fixture.Db.ChangeTracker.Clear();
        var ticketStatus = await fixture.Db.Set<ETicket>().AsNoTracking()
            .Where(item => item.OrderItemId == OracleWorkflowFixture.OriginalItemId)
            .Select(item => item.TicketStatus).SingleAsync();
        Assert.Contains(ticketStatus, new[] { "USED", "EXCHANGING" });
        Assert.Equal(ticketStatus == "EXCHANGING" ? 1 : 0,
            await fixture.Db.Set<ExchangeRequest>().AsNoTracking().CountAsync());

        async Task<bool> RunExchangeAsync()
        {
            await barrier.SignalAndWaitAsync(timeout.Token);
            return (await fixture.Application.CreateAsync(
                fixture.UserId,
                "oracle-exchange-racer",
                OracleWorkflowFixture.OriginalOrderId,
                fixture.CreateRequest,
                timeout.Token)).IsSuccess;
        }

        async Task<bool> RunRedeemAsync()
        {
            await barrier.SignalAndWaitAsync(timeout.Token);
            return (await redemption.RedeemAsync(
                "oracle-gate-racer",
                new RedeemTicketRequest("exgate-original-qr", "oracle-gate"),
                timeout.Token)).IsSuccess;
        }
    }

    [OracleExchangeFact]
    public async Task OracleExchangeWorkflow_RefundVersusExchange_OnlyOneFreezesOriginalTicket()
    {
        using var oracleGate = await OracleOrderTicketGate.EnterAsync();
        await EnsureMigrationAsync();
        await using var fixture = await OracleWorkflowFixture.CreateAsync(targetPrice: 125m);
        var (secondConnection, secondDb) = await OpenWorkflowDbAsync();
        await using var connectionLease = secondConnection;
        await using var dbLease = secondDb;
        var refund = new RefundApplicationService(
            secondDb,
            new RefundPolicyEngine(),
            fixture.TimeProvider,
            new OracleRefundLockCoordinator(secondDb),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<RefundApplicationService>.Instance,
            new NullOrderTicketAuditSink());
        var barrier = new OracleTwoPartyBarrier();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));

        var results = await Task.WhenAll(RunExchangeAsync(), RunRefundAsync());

        Assert.Single(results, result => result);
        fixture.Db.ChangeTracker.Clear();
        var ticketStatus = await fixture.Db.Set<ETicket>().AsNoTracking()
            .Where(item => item.OrderItemId == OracleWorkflowFixture.OriginalItemId)
            .Select(item => item.TicketStatus).SingleAsync();
        Assert.Contains(ticketStatus, new[] { "REFUNDING", "EXCHANGING" });
        Assert.Equal(1,
            await fixture.Db.Set<RefundRequest>().AsNoTracking().CountAsync() +
            await fixture.Db.Set<ExchangeRequest>().AsNoTracking().CountAsync());

        async Task<bool> RunExchangeAsync()
        {
            await barrier.SignalAndWaitAsync(timeout.Token);
            return (await fixture.Application.CreateAsync(
                fixture.UserId,
                "oracle-exchange-racer",
                OracleWorkflowFixture.OriginalOrderId,
                fixture.CreateRequest,
                timeout.Token)).IsSuccess;
        }

        async Task<bool> RunRefundAsync()
        {
            await barrier.SignalAndWaitAsync(timeout.Token);
            return (await refund.CreateAsync(
                fixture.UserId,
                "oracle-refund-racer",
                OracleWorkflowFixture.OriginalOrderId,
                new CreateRefundRequest(
                    [OracleWorkflowFixture.OriginalItemId], "oracle refund race"),
                timeout.Token)).IsSuccess;
        }
    }

    [OracleExchangeFact]
    public async Task OracleExchangeWorkflow_NormalOrderVersusExchange_OnlyOneConvertsTargetLock()
    {
        using var oracleGate = await OracleOrderTicketGate.EnterAsync();
        await EnsureMigrationAsync();
        await using var fixture = await OracleWorkflowFixture.CreateAsync(targetPrice: 125m);
        var (secondConnection, secondDb) = await OpenWorkflowDbAsync();
        await using var connectionLease = secondConnection;
        await using var dbLease = secondDb;
        var order = new OrderService(secondDb, fixture.TimeProvider);
        var barrier = new OracleTwoPartyBarrier();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));

        var results = await Task.WhenAll(RunExchangeAsync(), RunOrderAsync());

        Assert.Single(results, result => result);
        fixture.Db.ChangeTracker.Clear();
        Assert.Equal("CONVERTED", await fixture.Db.Set<SeatLock>().AsNoTracking()
            .Where(item => item.SeatLockId == OracleWorkflowFixture.TargetLockId)
            .Select(item => item.LockStatus).SingleAsync());
        Assert.Equal(1, await fixture.Db.Set<SeatReservation>().AsNoTracking()
            .CountAsync(item =>
                item.SessionId == OracleWorkflowFixture.TargetSessionId &&
                item.SeatId == OracleWorkflowFixture.TargetSeatId &&
                item.ReservationStatus == "ACTIVE"));

        async Task<bool> RunExchangeAsync()
        {
            await barrier.SignalAndWaitAsync(timeout.Token);
            return (await fixture.Application.CreateAsync(
                fixture.UserId,
                "oracle-exchange-racer",
                OracleWorkflowFixture.OriginalOrderId,
                fixture.CreateRequest,
                timeout.Token)).IsSuccess;
        }

        async Task<bool> RunOrderAsync()
        {
            await barrier.SignalAndWaitAsync(timeout.Token);
            return (await order.CreateAsync(
                fixture.UserId,
                "oracle-order-racer",
                new CreateOrderRequest(
                    OracleWorkflowFixture.TargetSessionId,
                    [new CreateOrderItemRequest(
                        OracleWorkflowFixture.TargetSeatId,
                        OracleWorkflowFixture.TargetStrategyId,
                        null,
                        "oracle-exchange-lock")],
                    null),
                timeout.Token)).IsSuccess;
        }
    }

    [OracleExchangeFact]
    public async Task OracleExchangeWorkflow_ApproveVersusReviewExpiration_SerializesTerminalState()
    {
        using var oracleGate = await OracleOrderTicketGate.EnterAsync();
        await EnsureMigrationAsync();
        await using var fixture = await OracleWorkflowFixture.CreateAsync(targetPrice: 125m);
        var created = await fixture.Application.CreateAsync(
            fixture.UserId,
            "oracle-user",
            OracleWorkflowFixture.OriginalOrderId,
            fixture.CreateRequest);
        Assert.True(created.IsSuccess, created.Message);
        var child = await fixture.Db.Set<Order>().SingleAsync(item => item.OrderType == "EXCHANGE");
        child.ExpireTime = fixture.TimeProvider.GetUtcNow().UtcDateTime;
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();
        var (secondConnection, secondDb) = await OpenWorkflowDbAsync();
        await using var connectionLease = secondConnection;
        await using var dbLease = secondDb;
        var secondReview = CreateOracleReview(secondDb, fixture.TimeProvider);
        var barrier = new OracleTwoPartyBarrier();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));

        var results = await Task.WhenAll(RunApproveAsync(), RunExpireAsync());

        Assert.Single(results, result => result);
        await AssertOracleFailedTerminalAsync(fixture);

        async Task<bool> RunApproveAsync()
        {
            await barrier.SignalAndWaitAsync(timeout.Token);
            return (await fixture.Review.ApproveAsync(
                "oracle-admin-racer",
                created.Value!.ExchangeId,
                new ApproveExchangeRequest(null),
                timeout.Token)).IsSuccess;
        }

        async Task<bool> RunExpireAsync()
        {
            await barrier.SignalAndWaitAsync(timeout.Token);
            return (await secondReview.ExpireAsync(
                created.Value!.ExchangeId,
                "oracle-expiration-racer",
                timeout.Token)).IsSuccess;
        }
    }

    [OracleExchangeFact]
    public async Task OracleExchangeWorkflow_PaymentVersusExpiration_SerializesTerminalState()
    {
        using var oracleGate = await OracleOrderTicketGate.EnterAsync();
        await EnsureMigrationAsync();
        await using var fixture = await OracleWorkflowFixture.CreateAsync(targetPrice: 125m);
        var created = await fixture.Application.CreateAsync(
            fixture.UserId,
            "oracle-user",
            OracleWorkflowFixture.OriginalOrderId,
            fixture.CreateRequest);
        var approved = await fixture.Review.ApproveAsync(
            "oracle-admin", created.Value!.ExchangeId, new ApproveExchangeRequest(null));
        Assert.True(approved.IsSuccess, approved.Message);
        var child = await fixture.Db.Set<Order>().SingleAsync(item => item.OrderType == "EXCHANGE");
        child.ExpireTime = fixture.TimeProvider.GetUtcNow().UtcDateTime;
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();
        var (secondConnection, secondDb) = await OpenWorkflowDbAsync();
        await using var connectionLease = secondConnection;
        await using var dbLease = secondDb;
        var secondReview = CreateOracleReview(secondDb, fixture.TimeProvider);
        var barrier = new OracleTwoPartyBarrier();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));

        var results = await Task.WhenAll(RunPaymentAsync(), RunExpireAsync());

        Assert.Single(results, result => result);
        await AssertOracleFailedTerminalAsync(fixture);
        fixture.Db.ChangeTracker.Clear();
        Assert.Empty(await fixture.Db.Set<Payment>().AsNoTracking()
            .Where(item => item.OrderId == child.OrderId).ToListAsync());

        async Task<bool> RunPaymentAsync()
        {
            await barrier.SignalAndWaitAsync(timeout.Token);
            return (await fixture.Payment.PayAsync(
                fixture.UserId,
                "oracle-payment-racer",
                created.Value!.ExchangeId,
                new ExchangePaymentRequest(PaymentChannel.ALIPAY, PaymentResult.SUCCESS),
                timeout.Token)).IsSuccess;
        }

        async Task<bool> RunExpireAsync()
        {
            await barrier.SignalAndWaitAsync(timeout.Token);
            return (await secondReview.ExpireAsync(
                created.Value!.ExchangeId,
                "oracle-expiration-racer",
                timeout.Token)).IsSuccess;
        }
    }

    [OracleExchangeTheory]
    [InlineData("reject")]
    [InlineData("review-expire")]
    [InlineData("payment-expire")]
    public async Task OracleExchangeWorkflow_RestorePathsReachDocumentedTerminalState(
        string path)
    {
        using var oracleGate = await OracleOrderTicketGate.EnterAsync();
        await EnsureMigrationAsync();
        await using var fixture = await OracleWorkflowFixture.CreateAsync(targetPrice: 125m);
        var created = await fixture.Application.CreateAsync(
            fixture.UserId,
            "oracle-user",
            OracleWorkflowFixture.OriginalOrderId,
            fixture.CreateRequest);
        Assert.True(created.IsSuccess, created.Message);

        OrderTicketResult<ExchangeResponse> terminal;
        if (path == "reject")
        {
            terminal = await fixture.Review.RejectAsync(
                "oracle-admin", created.Value!.ExchangeId,
                new RejectExchangeRequest("oracle rejection"));
        }
        else
        {
            if (path == "payment-expire")
            {
                var approved = await fixture.Review.ApproveAsync(
                    "oracle-admin", created.Value!.ExchangeId,
                    new ApproveExchangeRequest(null));
                Assert.True(approved.IsSuccess, approved.Message);
            }
            var child = await fixture.Db.Set<Order>()
                .SingleAsync(item => item.OrderType == "EXCHANGE");
            child.ExpireTime = fixture.TimeProvider.GetUtcNow().UtcDateTime;
            await fixture.Db.SaveChangesAsync();
            fixture.Db.ChangeTracker.Clear();
            terminal = await fixture.Review.ExpireAsync(
                created.Value!.ExchangeId, "oracle-expiration");
        }

        Assert.True(terminal.IsSuccess, terminal.Message);
        Assert.Equal(ExchangeStatus.FAILED, terminal.Value!.ExchangeStatus);
        Assert.Equal(
            path == "payment-expire"
                ? ExchangeApproveStatus.APPROVED
                : ExchangeApproveStatus.REJECTED,
            terminal.Value.ApproveStatus);
        Assert.Equal("UNUSED", await fixture.Db.Set<ETicket>().AsNoTracking()
            .Where(item => item.OrderItemId == OracleWorkflowFixture.OriginalItemId)
            .Select(item => item.TicketStatus).SingleAsync());
    }

    private static async Task EnsureMigrationAsync()
    {
        await using var connection = await OpenValidatedConnectionAsync();
        await ExecuteMigrationAsync(connection);
    }

    private static async Task<(OracleConnection Connection, OracleExchangeTestDbContext Db)>
        OpenWorkflowDbAsync()
    {
        var connection = await OpenValidatedConnectionAsync();
        try
        {
            var schema = await ScalarAsync<string>(
                connection,
                "SELECT SYS_CONTEXT('USERENV', 'CURRENT_SCHEMA') FROM DUAL");
            var options = new DbContextOptionsBuilder<OracleExchangeTestDbContext>()
                .UseOracle(connection, oracle =>
                {
                    oracle.CommandTimeout(45);
                    oracle.UseOracleSQLCompatibility(
                        OracleSQLCompatibility.DatabaseVersion21);
                })
                .ReplaceService<IModelCacheKeyFactory, PersonalSchemaModelCacheKeyFactory>()
                .AddInterceptors(new AppOwnerSqlGuardInterceptor())
                .Options;
            return (connection, new OracleExchangeTestDbContext(options, schema));
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private static ExchangeReviewService CreateOracleReview(
        AppDbContext db,
        TimeProvider timeProvider)
    {
        var application = new ExchangeApplicationService(
            db,
            new ExchangePolicyEngine(),
            timeProvider,
            new OracleExchangeLockCoordinator(db),
            Options.Create(new ExchangeOptions()));
        return new ExchangeReviewService(
            db,
            timeProvider,
            new OracleExchangeLockCoordinator(db),
            application,
            Options.Create(new ExchangeOptions()),
            new TicketIssuanceService(new OracleGateTicketTokenService()));
    }

    private static async Task AssertOracleFailedTerminalAsync(
        OracleWorkflowFixture fixture)
    {
        fixture.Db.ChangeTracker.Clear();
        var exchange = await fixture.Db.Set<ExchangeRequest>().AsNoTracking().SingleAsync();
        Assert.Equal("FAILED", exchange.ExchangeStatus);
        var child = await fixture.Db.Set<Order>().AsNoTracking()
            .SingleAsync(item => item.OrderType == "EXCHANGE");
        Assert.Equal("CANCELLED", child.OrderStatus);
        Assert.Equal("UNUSED", await fixture.Db.Set<ETicket>().AsNoTracking()
            .Where(item => item.OrderItemId == OracleWorkflowFixture.OriginalItemId)
            .Select(item => item.TicketStatus).SingleAsync());
        Assert.Equal("CANCELLED", await fixture.Db.Set<SeatReservation>().AsNoTracking()
            .Where(item => item.OrderItemId != OracleWorkflowFixture.OriginalItemId)
            .Select(item => item.ReservationStatus).SingleAsync());
    }

    private static async Task BreakMigrationBoundaryAsync(
        OracleConnection connection,
        string boundary)
    {
        switch (boundary)
        {
            case "ticket-temp-constraint":
                await ExecuteAsync(
                    connection,
                    "ALTER TABLE E_TICKET RENAME CONSTRAINT " +
                    "CHK_ETICKET_STATUS TO CHK_ETICKET_STATUS_NEW");
                break;
            case "policy-link-chain":
                await ExecuteAsync(
                    connection,
                    "ALTER TABLE EXCHANGE_REQUEST DROP CONSTRAINT " +
                    "FK_EXCHANGE_APPLIED_POLICY");
                await ExecuteAsync(connection, "DROP INDEX IDX_EXCHANGE_APPLIED_POLICY");
                await ExecuteAsync(
                    connection,
                    "ALTER TABLE EXCHANGE_REQUEST DROP COLUMN APPLIED_POLICY_ID");
                break;
            case "state-constraint":
                await ExecuteAsync(
                    connection,
                    "ALTER TABLE EXCHANGE_REQUEST DROP CONSTRAINT " +
                    "CHK_EXCHANGE_STATE_COMBO");
                break;
            case "item-index":
                await ExecuteAsync(connection, "DROP INDEX IDX_EXCHANGE_ITEM_ORDER");
                break;
            case "legacy-item-unique":
                await ExecuteAsync(
                    connection,
                    "ALTER TABLE EXCHANGE_ITEM ADD CONSTRAINT " +
                    "UK_EXCHANGE_ORDER_ITEM UNIQUE (ORDER_ITEM_ID)");
                break;
            case "policy-precision":
                await ExecuteAsync(
                    connection,
                    "ALTER TABLE EXCHANGE_POLICY MODIFY (" +
                    "ALLOW_CROSS_SESSION NUMBER(1) DEFAULT 1, " +
                    "STATUS NUMBER(1) DEFAULT 1)");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(boundary), boundary, null);
        }
    }

    private static async Task AssertMigrationTerminalStateAsync(
        OracleConnection connection)
    {
        Assert.Equal(1m, await ScalarAsync<decimal>(connection,
            "SELECT COUNT(*) FROM USER_TAB_COLUMNS " +
            "WHERE TABLE_NAME = 'EXCHANGE_REQUEST' " +
            "AND COLUMN_NAME = 'APPLIED_POLICY_ID' " +
            "AND DATA_TYPE = 'NUMBER' AND DATA_PRECISION = 19 " +
            "AND NVL(DATA_SCALE, 0) = 0 AND NULLABLE = 'Y'"));
        Assert.Equal(1m, await ScalarAsync<decimal>(connection,
            "SELECT COUNT(*) FROM USER_CONSTRAINTS " +
            "WHERE TABLE_NAME = 'EXCHANGE_REQUEST' " +
            "AND CONSTRAINT_NAME = 'FK_EXCHANGE_APPLIED_POLICY' " +
            "AND CONSTRAINT_TYPE = 'R'"));
        Assert.Equal(1m, await ScalarAsync<decimal>(connection,
            "SELECT COUNT(*) FROM USER_CONSTRAINTS " +
            "WHERE TABLE_NAME = 'EXCHANGE_REQUEST' " +
            "AND CONSTRAINT_NAME = 'CHK_EXCHANGE_STATE_COMBO' " +
            "AND CONSTRAINT_TYPE = 'C'"));
        Assert.Equal(0m, await ScalarAsync<decimal>(connection,
            "SELECT COUNT(*) FROM USER_CONSTRAINTS " +
            "WHERE TABLE_NAME = 'EXCHANGE_ITEM' " +
            "AND CONSTRAINT_NAME = 'UK_EXCHANGE_ORDER_ITEM'"));
        Assert.Equal(1m, await ScalarAsync<decimal>(connection,
            "SELECT COUNT(*) FROM USER_INDEXES " +
            "WHERE TABLE_NAME = 'EXCHANGE_ITEM' " +
            "AND INDEX_NAME = 'IDX_EXCHANGE_ITEM_ORDER' " +
            "AND UNIQUENESS = 'NONUNIQUE'"));
        Assert.Equal(1m, await ScalarAsync<decimal>(connection,
            "SELECT COUNT(*) FROM USER_CONSTRAINTS " +
            "WHERE TABLE_NAME = 'E_TICKET' " +
            "AND CONSTRAINT_NAME = 'CHK_ETICKET_STATUS' " +
            "AND INSTR(SEARCH_CONDITION_VC, '''EXCHANGING''') > 0"));
        Assert.Equal(2m, await ScalarAsync<decimal>(connection,
            "SELECT COUNT(*) FROM USER_TAB_COLUMNS " +
            "WHERE TABLE_NAME = 'EXCHANGE_POLICY' " +
            "AND COLUMN_NAME IN ('ALLOW_CROSS_SESSION', 'STATUS') " +
            "AND DATA_PRECISION = 3"));
    }

    private static async Task<OracleConnection> OpenValidatedConnectionAsync()
    {
        var raw = Environment.GetEnvironmentVariable(
            "SHOWTIME_ORACLE_EXCHANGE_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(raw))
        {
            throw new InvalidOperationException(
                "SHOWTIME_RUN_ORACLE_EXCHANGE_TESTS=1 requires " +
                "SHOWTIME_ORACLE_EXCHANGE_TEST_CONNECTION.");
        }

        var builder = new OracleConnectionStringBuilder(raw)
        {
            Pooling = false,
            ConnectionTimeout = 20,
        };
        var configured = ValidatePersonalSchema(builder.UserID);
        var connection = new OracleConnection(builder.ConnectionString);
        await connection.OpenAsync().WaitAsync(TimeSpan.FromSeconds(25));
        var sessionUser = ValidatePersonalSchema(await ScalarAsync<string>(
            connection,
            "SELECT SYS_CONTEXT('USERENV', 'SESSION_USER') FROM DUAL"));
        var currentSchema = ValidatePersonalSchema(await ScalarAsync<string>(
            connection,
            "SELECT SYS_CONTEXT('USERENV', 'CURRENT_SCHEMA') FROM DUAL"));
        if (!configured.Equals(sessionUser, StringComparison.OrdinalIgnoreCase) ||
            !configured.Equals(currentSchema, StringComparison.OrdinalIgnoreCase))
        {
            await connection.DisposeAsync();
            throw new InvalidOperationException(
                "Oracle exchange tests must remain in the configured personal schema.");
        }
        return connection;
    }

    private static async Task SeedRootOrderAsync(OracleConnection connection)
    {
        await CleanupRootOrderAsync(connection);
        var userId = await ScalarAsync<decimal>(
            connection,
            "SELECT USER_ID FROM SYS_USER WHERE ROWNUM = 1");
        var showId = await ScalarAsync<decimal>(
            connection,
            "SELECT SHOW_ID FROM SHOW WHERE ROWNUM = 1");
        var seatMapId = await ScalarAsync<decimal>(
            connection,
            "SELECT SEAT_MAP_ID FROM SEAT_MAP WHERE ROWNUM = 1");
        await ExecuteAsync(
            connection,
            "INSERT INTO SHOW_SESSION (" +
            "SESSION_ID, SHOW_ID, SEAT_MAP_ID, START_TIME, END_TIME, " +
            "SALE_START_TIME, SALE_END_TIME, SESSION_STATUS, CREATE_BY, UPDATE_BY) " +
            "VALUES (:sessionId, :showId, :seatMapId, " +
            "SYSTIMESTAMP + INTERVAL '7' DAY, " +
            "SYSTIMESTAMP + INTERVAL '7' DAY + INTERVAL '2' HOUR, " +
            "SYSTIMESTAMP - INTERVAL '1' DAY, " +
            "SYSTIMESTAMP + INTERVAL '6' DAY, 'ONSALE', 'oracle-exchange-gate', " +
            "'oracle-exchange-gate')",
            ("sessionId", OracleDbType.Int64, (object)SessionId),
            ("showId", OracleDbType.Int64, showId),
            ("seatMapId", OracleDbType.Int64, seatMapId));
        await ExecuteAsync(
            connection,
            "INSERT INTO T_ORDER (" +
            "ORDER_ID, ORDER_NO, USER_ID, SESSION_ID, ORDER_TYPE, TOTAL_AMOUNT, " +
            "DISCOUNT_AMOUNT, TICKET_COUNT, ORDER_STATUS, EXPIRE_TIME, SOURCE, " +
            "CREATE_BY, UPDATE_BY) VALUES (" +
            ":orderId, 'ORACLE-EXCHANGE-GATE-998810001', :userId, :sessionId, " +
            "'NORMAL', 1.00, 0.00, 1, 'PAID', SYSTIMESTAMP + INTERVAL '1' DAY, " +
            "'WEB', 'oracle-exchange-gate', 'oracle-exchange-gate')",
            ("orderId", OracleDbType.Int64, (object)OrderId),
            ("userId", OracleDbType.Int64, userId),
            ("sessionId", OracleDbType.Int64, (object)SessionId));
        await ExecuteAsync(connection, "COMMIT");
    }

    private static async Task CleanupRootOrderAsync(OracleConnection connection)
    {
        await ExecuteAsync(
            connection,
            "DELETE FROM EXCHANGE_REQUEST WHERE EXCHANGE_ID = :id",
            ("id", OracleDbType.Int64, (object)ExchangeId));
        await ExecuteAsync(
            connection,
            "DELETE FROM EXCHANGE_POLICY WHERE POLICY_ID = :id",
            ("id", OracleDbType.Int64, (object)PolicyId));
        await ExecuteAsync(
            connection,
            "DELETE FROM T_ORDER WHERE ORDER_ID = :id",
            ("id", OracleDbType.Int64, (object)OrderId));
        await ExecuteAsync(
            connection,
            "DELETE FROM SHOW_SESSION WHERE SESSION_ID = :id",
            ("id", OracleDbType.Int64, (object)SessionId));
        await ExecuteAsync(connection, "COMMIT");
    }

    private static async Task ExecuteMigrationAsync(OracleConnection connection)
    {
        var migrationPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "../../../../db/migrations/20260830__exchange_workflow_support.sql"));
        var lines = await File.ReadAllLinesAsync(migrationPath);
        var block = new List<string>();
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("SET ", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("WHENEVER ", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            if (trimmed == "/")
            {
                var sql = string.Join(Environment.NewLine, block).Trim();
                if (sql.Length > 0)
                    await ExecuteAsync(connection, sql);
                block.Clear();
                continue;
            }
            block.Add(line);
        }
        var trailingSql = string.Join(Environment.NewLine, block).Trim();
        if (trailingSql.Length > 0 &&
            !trailingSql.StartsWith(
                "SELECT SYS_CONTEXT('USERENV', 'SESSION_USER')",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The exchange migration contains an unterminated SQL*Plus block.");
        }
    }

    private static async Task ExecuteAsync(
        OracleConnection connection,
        string sql,
        params (string Name, OracleDbType Type, object Value)[] parameters)
    {
        AppOwnerSqlGuardInterceptor.EnsureSafeCommandText(sql);
        await using var command = connection.CreateCommand();
        command.BindByName = true;
        command.CommandText = sql;
        foreach (var parameter in parameters)
        {
            command.Parameters.Add(new OracleParameter(
                parameter.Name,
                parameter.Type,
                parameter.Value,
                ParameterDirection.Input));
        }
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<T> ScalarAsync<T>(
        OracleConnection connection,
        string sql)
    {
        AppOwnerSqlGuardInterceptor.EnsureSafeCommandText(sql);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        var value = await command.ExecuteScalarAsync();
        return (T)Convert.ChangeType(value!, typeof(T));
    }

    private static string ValidatePersonalSchema(string? value)
    {
        var identifier = value?.Trim().ToUpperInvariant();
        if (!string.Equals(identifier, "LEIKAI", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Oracle exchange tests require a safe personal schema.");
        }
        return identifier!;
    }

    private sealed class AppOwnerSqlGuardInterceptor : DbCommandInterceptor
    {
        public static void EnsureSafeCommandText(string commandText)
        {
            if (Regex.IsMatch(
                    commandText,
                    "(?:\\\"APP_OWNER\\\"|APP_OWNER)\\s*\\.",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                throw new InvalidOperationException(
                    "Oracle exchange tests refuse APP_OWNER-qualified SQL.");
            }
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

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            EnsureSafeCommandText(command.CommandText);
            return ValueTask.FromResult(result);
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
    }

    private sealed class OracleExchangeFactAttribute : FactAttribute
    {
        public OracleExchangeFactAttribute()
        {
            if (Environment.GetEnvironmentVariable(
                    "SHOWTIME_RUN_ORACLE_EXCHANGE_TESTS") != "1")
            {
                Skip = "SHOWTIME_RUN_ORACLE_EXCHANGE_TESTS is not 1; " +
                       "no Oracle exchange connection will be opened.";
            }
        }
    }

    private sealed class OracleExchangeTheoryAttribute : TheoryAttribute
    {
        public OracleExchangeTheoryAttribute()
        {
            if (Environment.GetEnvironmentVariable(
                    "SHOWTIME_RUN_ORACLE_EXCHANGE_TESTS") != "1")
            {
                Skip = "SHOWTIME_RUN_ORACLE_EXCHANGE_TESTS is not 1; " +
                       "no Oracle exchange connection will be opened.";
            }
        }
    }

    private sealed class OracleWorkflowFixture : IAsyncDisposable
    {
        public const long OriginalOrderId = 998_820_001;
        public const long OriginalItemId = 998_820_001;
        private const long OriginalSessionId = 998_820_001;
        public const long TargetSessionId = 998_820_002;
        private const long SeatSectionId = 998_820_001;
        private const long OriginalSeatId = 998_820_001;
        public const long TargetSeatId = 998_820_002;
        private const long OriginalStrategyId = 998_820_001;
        public const long TargetStrategyId = 998_820_002;
        private const long OriginalPaymentId = 998_820_001;
        private const long OriginalTicketId = 998_820_001;
        private const long OriginalReservationId = 998_820_001;
        public const long TargetLockId = 998_820_001;
        private const long WorkflowPolicyId = 998_820_001;
        private const long WorkflowRefundPolicyId = 998_820_002;

        private readonly OracleConnection connection;

        private OracleWorkflowFixture(
            OracleConnection connection,
            OracleExchangeTestDbContext db,
            long userId,
            FixedTimeProvider timeProvider,
            decimal targetPrice)
        {
            this.connection = connection;
            Db = db;
            UserId = userId;
            TimeProvider = timeProvider;
            var policyEngine = new ExchangePolicyEngine();
            var locks = new OracleExchangeLockCoordinator(db);
            Application = new ExchangeApplicationService(
                db, policyEngine, timeProvider, locks,
                Options.Create(new ExchangeOptions()));
            var issuance = new TicketIssuanceService(new OracleGateTicketTokenService());
            Review = new ExchangeReviewService(
                db, timeProvider, locks, Application,
                Options.Create(new ExchangeOptions()), issuance);
            Payment = new ExchangePaymentService(
                db, timeProvider, locks, Application, Review, issuance);
            CreateRequest = new CreateExchangeRequest(
                TargetSessionId,
                [new ExchangeTargetItemRequest(
                    OriginalItemId,
                    TargetSeatId,
                    TargetStrategyId,
                    "oracle-exchange-lock")],
                $"oracle target {targetPrice}");
        }

        public OracleExchangeTestDbContext Db { get; }
        public long UserId { get; }
        public FixedTimeProvider TimeProvider { get; }
        public ExchangeApplicationService Application { get; }
        public ExchangeReviewService Review { get; }
        public ExchangePaymentService Payment { get; }
        public CreateExchangeRequest CreateRequest { get; }

        public static async Task<OracleWorkflowFixture> CreateAsync(decimal targetPrice)
        {
            var connection = await OpenValidatedConnectionAsync();
            try
            {
                await CleanupWorkflowAsync(connection);
                var schema = await ScalarAsync<string>(connection,
                    "SELECT SYS_CONTEXT('USERENV', 'CURRENT_SCHEMA') FROM DUAL");
                var userId = Convert.ToInt64(await ScalarAsync<decimal>(connection,
                    "SELECT USER_ID FROM SYS_USER WHERE ROWNUM = 1"));
                var showId = Convert.ToInt64(await ScalarAsync<decimal>(connection,
                    "SELECT SHOW_ID FROM SHOW WHERE ROWNUM = 1"));
                var seatMapId = Convert.ToInt64(await ScalarAsync<decimal>(connection,
                    "SELECT SEAT_MAP_ID FROM SEAT_MAP WHERE ROWNUM = 1"));
                var options = new DbContextOptionsBuilder<OracleExchangeTestDbContext>()
                    .UseOracle(connection, oracle =>
                    {
                        oracle.CommandTimeout(20);
                        oracle.UseOracleSQLCompatibility(
                            OracleSQLCompatibility.DatabaseVersion21);
                    })
                    .ReplaceService<IModelCacheKeyFactory, PersonalSchemaModelCacheKeyFactory>()
                    .AddInterceptors(new AppOwnerSqlGuardInterceptor())
                    .Options;
                var db = new OracleExchangeTestDbContext(options, schema);
                var now = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc);
                var timeProvider = new FixedTimeProvider(new DateTimeOffset(now));
                db.AddRange(
                    new ShowtimeBackend.Entities.ShowSession.ShowSession
                    {
                        SessionId = OriginalSessionId,
                        ShowId = showId,
                        SeatMapId = seatMapId,
                        StartTime = now.AddDays(7),
                        EndTime = now.AddDays(7).AddHours(2),
                        SaleStartTime = now.AddDays(-1),
                        SaleEndTime = now.AddDays(6),
                        SessionStatus = "ONSALE",
                        CreateBy = "oracle-gate",
                        UpdateBy = "oracle-gate",
                    },
                    new ShowtimeBackend.Entities.ShowSession.ShowSession
                    {
                        SessionId = TargetSessionId,
                        ShowId = showId,
                        SeatMapId = seatMapId,
                        StartTime = now.AddDays(8),
                        EndTime = now.AddDays(8).AddHours(2),
                        SaleStartTime = now.AddDays(-1),
                        SaleEndTime = now.AddDays(7),
                        SessionStatus = "ONSALE",
                        CreateBy = "oracle-gate",
                        UpdateBy = "oracle-gate",
                    },
                    new SeatSection
                    {
                        SeatSectionId = SeatSectionId,
                        SeatMapId = seatMapId,
                        SectionCode = "EXGATE",
                        SectionName = "Exchange gate",
                        CreateBy = "oracle-gate",
                        UpdateBy = "oracle-gate",
                    },
                    new Seat
                    {
                        SeatId = OriginalSeatId,
                        SeatSectionId = SeatSectionId,
                        RowCode = "A",
                        SeatNo = "1",
                        RowIndex = 1,
                        ColIndex = 1,
                        CreateBy = "oracle-gate",
                        UpdateBy = "oracle-gate",
                    },
                    new Seat
                    {
                        SeatId = TargetSeatId,
                        SeatSectionId = SeatSectionId,
                        RowCode = "A",
                        SeatNo = "2",
                        RowIndex = 1,
                        ColIndex = 2,
                        CreateBy = "oracle-gate",
                        UpdateBy = "oracle-gate",
                    },
                    new PriceStrategy
                    {
                        PriceStrategyId = OriginalStrategyId,
                        SessionId = OriginalSessionId,
                        SeatSectionId = SeatSectionId,
                        StrategyName = "original gate",
                        Price = 105m,
                        SaleStartTime = now.AddDays(-1),
                        SaleEndTime = now.AddDays(6),
                        CreateBy = "oracle-gate",
                        UpdateBy = "oracle-gate",
                    },
                    new PriceStrategy
                    {
                        PriceStrategyId = TargetStrategyId,
                        SessionId = TargetSessionId,
                        SeatSectionId = SeatSectionId,
                        StrategyName = "target gate",
                        Price = targetPrice,
                        SaleStartTime = now.AddDays(-1),
                        SaleEndTime = now.AddDays(7),
                        CreateBy = "oracle-gate",
                        UpdateBy = "oracle-gate",
                    },
                    new Order
                    {
                        OrderId = OriginalOrderId,
                        OrderNo = "EXGATE-ORDER-998820001",
                        UserId = userId,
                        SessionId = OriginalSessionId,
                        TotalAmount = 105m,
                        TicketCount = 1,
                        OrderStatus = "ISSUED",
                        ExpireTime = now.AddDays(-1),
                        PayTime = now.AddDays(-1),
                        IssueTime = now.AddDays(-1),
                        Source = "WEB",
                        CreateBy = "oracle-gate",
                        UpdateBy = "oracle-gate",
                    },
                    new OrderItem
                    {
                        OrderItemId = OriginalItemId,
                        OrderId = OriginalOrderId,
                        SeatId = OriginalSeatId,
                        PriceStrategyId = OriginalStrategyId,
                        UnitPrice = 105m,
                        ItemStatus = "NORMAL",
                        CreateBy = "oracle-gate",
                        UpdateBy = "oracle-gate",
                    },
                    new Payment
                    {
                        PaymentId = OriginalPaymentId,
                        PaymentNo = "EXGATE-PAY-998820001",
                        OrderId = OriginalOrderId,
                        UserId = userId,
                        PayAmount = 105m,
                        PayChannel = "ALIPAY",
                        PayStatus = "SUCCESS",
                        PayTime = now.AddDays(-1),
                        CreateBy = "oracle-gate",
                        UpdateBy = "oracle-gate",
                    },
                    new ETicket
                    {
                        ETicketId = OriginalTicketId,
                        ETicketNo = "EXGATE-TKT-998820001",
                        OrderItemId = OriginalItemId,
                        UserId = userId,
                        QrCode = "exgate-original-qr",
                        AntiFakeCode = "exgate-original-anti",
                        TicketStatus = "UNUSED",
                        CreateBy = "oracle-gate",
                        UpdateBy = "oracle-gate",
                    },
                    new SeatReservation
                    {
                        SeatReservationId = OriginalReservationId,
                        SessionId = OriginalSessionId,
                        SeatId = OriginalSeatId,
                        OrderItemId = OriginalItemId,
                        ReservationType = "ORDER",
                        ReservationStatus = "ACTIVE",
                        ReserveTime = now.AddDays(-1),
                        CreateBy = "oracle-gate",
                        UpdateBy = "oracle-gate",
                    },
                    new SeatLock
                    {
                        SeatLockId = TargetLockId,
                        SessionId = TargetSessionId,
                        SeatId = TargetSeatId,
                        UserId = userId,
                        LockToken = "oracle-exchange-lock",
                        LockStatus = "ACTIVE",
                        LockTime = now,
                        ExpireTime = now.AddHours(1),
                        CreateTime = now,
                        CreateBy = "oracle-gate",
                        UpdateBy = "oracle-gate",
                    },
                    new ExchangePolicy
                    {
                        PolicyId = WorkflowPolicyId,
                        ShowId = showId,
                        PolicyName = "oracle workflow gate",
                        ExchangeDeadlineHour = 24,
                        ExchangeFee = 5m,
                        AllowCrossSession = 1,
                        Priority = 100,
                        Status = 1,
                        CreateBy = "oracle-gate",
                        UpdateBy = "oracle-gate",
                    },
                    new RefundPolicy
                    {
                        PolicyId = WorkflowRefundPolicyId,
                        ShowId = showId,
                        PolicyName = "oracle refund race gate",
                        RefundDeadlineHour = 0,
                        RefundRate = 1m,
                        ServiceFee = 0m,
                        Priority = 100,
                        Status = 1,
                        CreateBy = "oracle-gate",
                        UpdateBy = "oracle-gate",
                    });
                var seedEntities = db.ChangeTracker.Entries()
                    .Select(entry => entry.Entity)
                    .ToArray();
                db.ChangeTracker.Clear();
                TrackStage(entity =>
                    entity is ShowtimeBackend.Entities.ShowSession.ShowSession or SeatSection);
                await db.SaveChangesAsync();
                db.ChangeTracker.Clear();
                TrackStage(entity => entity is Seat);
                await db.SaveChangesAsync();
                db.ChangeTracker.Clear();
                TrackStage(entity =>
                    entity is PriceStrategy or Order or ExchangePolicy or RefundPolicy);
                await db.SaveChangesAsync();
                db.ChangeTracker.Clear();
                TrackStage(entity =>
                    entity is OrderItem or ShowtimeBackend.Entities.OrderTicket.Payment or SeatLock);
                await db.SaveChangesAsync();
                db.ChangeTracker.Clear();
                TrackStage(entity => entity is ETicket or SeatReservation);
                await db.SaveChangesAsync();
                db.ChangeTracker.Clear();
                return new OracleWorkflowFixture(
                    connection, db, userId, timeProvider, targetPrice);

                void TrackStage(Func<object, bool> predicate)
                {
                    foreach (var entity in seedEntities.Where(predicate))
                        db.Entry(entity).State = EntityState.Added;
                }
            }
            catch
            {
                await CleanupWorkflowAsync(connection);
                await connection.DisposeAsync();
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            Db.ChangeTracker.Clear();
            await CleanupWorkflowAsync(connection);
            await Db.DisposeAsync();
            await connection.DisposeAsync();
        }

        private static async Task CleanupWorkflowAsync(OracleConnection connection)
        {
            await ExecuteAsync(connection, "ROLLBACK");
            string[] statements =
            [
                "DELETE FROM E_TICKET WHERE ORDER_ITEM_ID IN (SELECT ORDER_ITEM_ID FROM ORDER_ITEM WHERE ORDER_ID = 998820001 OR ORDER_ID IN (SELECT ORDER_ID FROM T_ORDER WHERE PARENT_ORDER_ID = 998820001))",
                "DELETE FROM SEAT_RESERVATION WHERE SEAT_LOCK_ID = 998820001 OR ORDER_ITEM_ID IN (SELECT ORDER_ITEM_ID FROM ORDER_ITEM WHERE ORDER_ID = 998820001 OR ORDER_ID IN (SELECT ORDER_ID FROM T_ORDER WHERE PARENT_ORDER_ID = 998820001))",
                "DELETE FROM EXCHANGE_ITEM WHERE EXCHANGE_ID IN (SELECT EXCHANGE_ID FROM EXCHANGE_REQUEST WHERE ORDER_ID = 998820001)",
                "DELETE FROM EXCHANGE_REQUEST WHERE ORDER_ID = 998820001",
                "DELETE FROM PAYMENT WHERE ORDER_ID = 998820001 OR ORDER_ID IN (SELECT ORDER_ID FROM T_ORDER WHERE PARENT_ORDER_ID = 998820001)",
                "DELETE FROM ORDER_ITEM WHERE ORDER_ID IN (SELECT ORDER_ID FROM T_ORDER WHERE SESSION_ID = 998820002 AND CREATE_BY = 'oracle-order-racer')",
                "DELETE FROM ORDER_ITEM WHERE ORDER_ID IN (SELECT ORDER_ID FROM T_ORDER WHERE PARENT_ORDER_ID = 998820001)",
                "DELETE FROM ORDER_ITEM WHERE ORDER_ID = 998820001",
                "DELETE FROM T_ORDER WHERE SESSION_ID = 998820002 AND CREATE_BY = 'oracle-order-racer'",
                "DELETE FROM T_ORDER WHERE PARENT_ORDER_ID = 998820001",
                "DELETE FROM T_ORDER WHERE ORDER_ID = 998820001",
                "DELETE FROM SEAT_LOCK WHERE SEAT_LOCK_ID = 998820001",
                "DELETE FROM PRICE_STRATEGY WHERE PRICE_STRATEGY_ID IN (998820001, 998820002)",
                "DELETE FROM SEAT WHERE SEAT_ID IN (998820001, 998820002)",
                "DELETE FROM EXCHANGE_POLICY WHERE POLICY_ID = 998820001",
                "DELETE FROM REFUND_POLICY WHERE POLICY_ID = 998820002",
                "DELETE FROM SEAT_SECTION WHERE SEAT_SECTION_ID = 998820001",
                "DELETE FROM SHOW_SESSION WHERE SESSION_ID IN (998820001, 998820002)",
                "COMMIT",
            ];
            foreach (var statement in statements)
                await ExecuteAsync(connection, statement);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class ExistingOracleTicketTokenService(
        string ticketNo,
        string expectedQrCode) : ITicketTokenService
    {
        public TicketCredential Generate(DateTimeOffset issuedAt) =>
            throw new NotSupportedException();

        public bool TryValidate(string qrCode, out TicketTokenPayload? payload)
        {
            payload = string.Equals(qrCode, expectedQrCode, StringComparison.Ordinal)
                ? new TicketTokenPayload(ticketNo, 0, "oracle-concurrency")
                : null;
            return payload is not null;
        }
    }

    private sealed class OracleTwoPartyBarrier
    {
        private readonly TaskCompletionSource release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int arrivals;

        public async Task SignalAndWaitAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref arrivals) == 2)
                release.TrySetResult();
            await release.Task.WaitAsync(cancellationToken);
        }
    }

    private sealed class OracleGateTicketTokenService : ITicketTokenService
    {
        public TicketCredential Generate(DateTimeOffset issuedAt)
        {
            var suffix = Guid.NewGuid().ToString("N")[..12];
            return new TicketCredential(
                $"EXGATE-{suffix}", $"EXANTI-{suffix}", $"EXQR-{suffix}");
        }

        public bool TryValidate(string qrCode, out TicketTokenPayload? payload)
        {
            payload = null;
            return false;
        }
    }

    private sealed class OracleExchangeTestDbContext(
        DbContextOptions<OracleExchangeTestDbContext> options,
        string schema) : AppDbContext(options)
    {
        public string PersonalSchema { get; } = ValidatePersonalSchema(schema);

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.HasDefaultSchema(PersonalSchema);
        }
    }

    private sealed class PersonalSchemaModelCacheKeyFactory : IModelCacheKeyFactory
    {
        public object Create(DbContext context, bool designTime) => context is
            OracleExchangeTestDbContext personal
                ? (context.GetType(), personal.PersonalSchema, designTime)
                : (object)(context.GetType(), designTime);
    }
}
