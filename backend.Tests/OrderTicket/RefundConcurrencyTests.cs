using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Oracle.ManagedDataAccess.Client;
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
    private const long OracleRefundOrderId = 998_830_001;
    private const long OracleRefundPaymentId = 998_830_001;
    private const long OracleRefundUserId = 998_830_001;
    private const long OracleRefundSessionId = 998_830_001;
    private const string OracleRefundMarker = "oracle-refund-gate";

    [Fact]
    public async Task ApproveAndReject_FromSameOriginalState_OnlyOneSaveSucceeds()
    {
        await using var database = await RefundTestData.CreateSharedSqliteAsync();
        await using var approveDb = database.CreateContext();
        await using var rejectDb = database.CreateContext();
        Assert.NotSame(approveDb, rejectDb);
        Assert.NotSame(
            approveDb.Database.GetDbConnection(),
            rejectDb.Database.GetDbConnection());
        var approve = await approveDb.Set<RefundRequest>().SingleAsync();
        var reject = await rejectDb.Set<RefundRequest>().SingleAsync();

        approve.ApproveStatus = "APPROVED";
        approve.RefundStatus = "COMPLETED";
        reject.ApproveStatus = "REJECTED";
        reject.RefundStatus = "FAILED";

        await approveDb.SaveChangesAsync();
        var exception = await Assert.ThrowsAsync<DbUpdateConcurrencyException>(
            () => rejectDb.SaveChangesAsync());

        Assert.Single(exception.Entries);
        Assert.IsType<RefundRequest>(exception.Entries[0].Entity);
        await using var verificationDb = database.CreateContext();
        var persisted = await verificationDb.Set<RefundRequest>()
            .AsNoTracking()
            .SingleAsync();
        Assert.Equal("APPROVED", persisted.ApproveStatus);
        Assert.Equal("COMPLETED", persisted.RefundStatus);
    }

    [Theory]
    [InlineData("order-item")]
    [InlineData("ticket")]
    [InlineData("order")]
    public async Task EntityStateTokens_FromSameOriginalState_RejectSecondSave(
        string token)
    {
        await using var database = await RefundTestData.CreateSharedSqliteAsync();
        await using var firstDb = database.CreateContext();
        await using var secondDb = database.CreateContext();

        switch (token)
        {
            case "order-item":
                var firstItem = await firstDb.Set<OrderItem>().SingleAsync();
                var secondItem = await secondDb.Set<OrderItem>().SingleAsync();
                firstItem.ItemStatus = "REFUNDED";
                secondItem.ItemStatus = "NORMAL";
                break;
            case "ticket":
                var firstTicket = await firstDb.Set<ETicket>().SingleAsync();
                var secondTicket = await secondDb.Set<ETicket>().SingleAsync();
                firstTicket.TicketStatus = "REFUNDED";
                secondTicket.TicketStatus = "UNUSED";
                break;
            case "order":
                var firstOrder = await firstDb.Set<Order>().SingleAsync();
                var secondOrder = await secondDb.Set<Order>().SingleAsync();
                firstOrder.OrderStatus = "PART_REFUND";
                secondOrder.OrderStatus = "REFUNDED";
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(token), token, null);
        }

        await firstDb.SaveChangesAsync();
        var exception = await Assert.ThrowsAsync<DbUpdateConcurrencyException>(
            () => secondDb.SaveChangesAsync());

        Assert.Single(exception.Entries);
    }

    [Fact]
    public async Task DifferentItemApprovals_ForSameOrder_EndAsFullyRefundedWithoutLostUpdate()
    {
        await using var fixture = await RefundTestData.CreateTwoPendingRefundsAsync();

        var first = await fixture.ApproveWithFreshContextAsync(fixture.RefundIds[0]);
        var afterFirst = await fixture.OrderStatusAsync();
        var second = await fixture.ApproveWithFreshContextAsync(fixture.RefundIds[1]);

        Assert.True(first.IsSuccess);
        Assert.Equal("PART_REFUND", afterFirst);
        Assert.True(second.IsSuccess);
        Assert.Equal("REFUNDED", await fixture.OrderStatusAsync());
        Assert.Equal(
            first.Value!.ActualRefund!.Value + second.Value!.ActualRefund!.Value,
            await fixture.PaymentRefundAmountAsync());
    }

    [Theory]
    [InlineData("order-item", typeof(OrderItem))]
    [InlineData("ticket", typeof(ETicket))]
    [InlineData("order", typeof(Order))]
    public async Task ApproveAsync_WhenEntityTokenChangesAfterBulkDml_RollsBackEverything(
        string token,
        Type expectedConcurrentEntityType)
    {
        await using var fixture = await RefundTestData.CreatePendingRefundAsync();
        await MakeSingleItemFinancialsConsistentAsync(fixture);
        var observer = new RefundBulkUpdateObserver();
        var concurrency = new ApproveEntityConcurrencyInterceptor(token);
        await using var db = fixture.CreateDbContext(observer, concurrency);
        var service = CreateReviewService(db, fixture.TimeProvider);

        var result = await service.ApproveAsync(
            "admin",
            fixture.RefundId,
            new ApproveRefundRequest(null),
            CancellationToken.None);

        Assert.Equal(OrderTicketFailure.Conflict, result.Failure);
        Assert.Equal("REFUND_REVIEW_CONFLICT", result.ErrorCode);
        Assert.Equal(1, observer.PaymentUpdateRows);
        Assert.Equal(1, observer.ReservationUpdateRows);
        Assert.Equal(1, concurrency.MutationAffectedRows);
        Assert.True(concurrency.MutationObservedTransaction);
        Assert.Equal(1, concurrency.ConcurrencyExceptionObservedCount);
        Assert.Contains(expectedConcurrentEntityType, concurrency.ConcurrentEntityTypes);
        Assert.Empty(db.ChangeTracker.Entries());
        Assert.Null(db.Database.CurrentTransaction);
        Assert.Equal(0m, await fixture.PaymentRefundAmountAsync());
        Assert.Equal("ACTIVE", await fixture.ReservationStatusAsync());
        Assert.Equal("REFUNDING", await fixture.ItemStatusAsync());
        Assert.Equal("REFUNDING", await fixture.TicketStatusAsync());
        Assert.Equal("ISSUED", await fixture.OrderStatusAsync());
        Assert.Equal("PENDING", await fixture.RefundApproveStatusAsync());
        Assert.Equal("PENDING", await fixture.Db.Set<RefundRequest>()
            .AsNoTracking()
            .Where(item => item.RefundId == fixture.RefundId)
            .Select(item => item.RefundStatus)
            .SingleAsync());
    }

    [Fact]
    public async Task ApproveAsync_UsesRefundThenOrderLockAndStableItemOrder()
    {
        await using var fixture = await RefundTestData.CreatePendingRefundAsync(
            itemCount: 2,
            reverseRefundItemSeed: true);
        var seededOrder = await fixture.Db.Set<RefundItem>()
            .AsNoTracking()
            .OrderBy(item => item.RefundItemId)
            .Select(item => item.OrderItemId)
            .ToListAsync();
        Assert.Equal(fixture.OrderItemIds.Reverse(), seededOrder);

        var itemOrderObserver = new StableRefundItemOrderObserver();
        await using var db = fixture.CreateDbContext(itemOrderObserver);
        var coordinator = new RecordingRefundLockCoordinator(db);
        var service = new RefundReviewService(
            db,
            fixture.TimeProvider,
            coordinator,
            NullLogger<RefundReviewService>.Instance,
            fixture.AuditSink);

        var result = await service.ApproveAsync(
            "admin",
            fixture.RefundId,
            new ApproveRefundRequest(null),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(["refund:401", "order:11"], coordinator.Calls);
        Assert.All(coordinator.TransactionObserved, Assert.True);
        Assert.True(
            fixture.OrderItemIds.SequenceEqual(itemOrderObserver.ReservationOrderItemIds),
            $"Expected [{string.Join(", ", fixture.OrderItemIds)}], observed " +
            $"[{string.Join(", ", itemOrderObserver.ReservationOrderItemIds)}]; " +
            $"parameters: {string.Join("; ", itemOrderObserver.ParameterValues)}; " +
            $"SQL: {itemOrderObserver.ReservationCommandText}");
    }

    [Fact]
    public async Task CreateAsync_UsesOnlyOrderLockAndStableItemOrder()
    {
        await using var fixture = await RefundTestData.CreateIssuedOrderAsync();
        fixture.Db.Add(new RefundPolicy
        {
            PolicyId = 801,
            PolicyName = "全局",
            RefundDeadlineHour = 24,
            RefundRate = 0.8m,
            ServiceFee = 0m,
            Priority = 1,
            Status = 1,
        });
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();
        var coordinator = new RecordingRefundLockCoordinator(fixture.Db);

        var result = await CreateService(fixture.Db, coordinator).CreateAsync(
            fixture.UserId,
            "alice",
            fixture.OrderId,
            new CreateRefundRequest(fixture.OrderItemIds.Reverse().ToArray(), "行程变更"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(["order:11"], coordinator.Calls);
        Assert.All(coordinator.TransactionObserved, Assert.True);
        Assert.Equal(
            fixture.OrderItemIds,
            result.Value!.Items.Select(item => item.OrderItemId));
    }

    [Fact]
    public async Task OracleRefundLockCoordinator_OnSqlite_AcquiresAtomicWriteLocks()
    {
        await using var fixture = await RefundTestData.CreateIssuedOrderAsync();
        fixture.Db.Add(new RefundRequest
        {
            RefundId = 401,
            RefundNo = "REFUND-LOCK-401",
            OrderId = fixture.OrderId,
            UserId = fixture.UserId,
            RefundType = "FULL",
            RefundAmount = 105m,
            ActualRefund = 105m,
            FeeRate = 1m,
            AppliedServiceFee = 0m,
            ApproveStatus = "PENDING",
            RefundStatus = "PENDING",
        });
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();
        await using var transaction = await fixture.Db.Database.BeginTransactionAsync();
        var coordinator = new OracleRefundLockCoordinator(fixture.Db);

        Assert.True(await coordinator.LockRefundRequestAsync(401, CancellationToken.None));
        Assert.True(await coordinator.LockOrderAsync(fixture.OrderId, CancellationToken.None));
        Assert.False(await coordinator.LockOrderAsync(999_999, CancellationToken.None));
        Assert.NotNull(fixture.Db.Database.CurrentTransaction);
        await transaction.RollbackAsync();
    }

    [Theory]
    [InlineData("APP_OWNER")]
    [InlineData("deploy_user")]
    [InlineData("PERSONAL_SCHEMA.T_ORDER")]
    [InlineData("PERSONAL_SCHEMA\" WHERE 1=1 --")]
    [InlineData("9PERSONAL")]
    public void OracleRefundConcurrency_RejectsSharedOrUnsafeSchemaBeforeConnecting(
        string configuredUser)
    {
        Assert.Throws<InvalidOperationException>(
            () => ValidatePersonalOracleIdentifier(configuredUser));
    }

    [OracleRefundFact]
    public async Task OracleRefundConcurrency_WhenPersonalSchemaIsExplicitlyConfigured()
    {
        using var oracleGate = await OracleOrderTicketGate.EnterAsync();
        var connectionString = Environment.GetEnvironmentVariable(
            "SHOWTIME_ORACLE_REFUND_TEST_CONNECTION") ??
            throw new InvalidOperationException(
                "The Oracle test was enabled without an explicit connection string.");

        var connectionBuilder = new OracleConnectionStringBuilder(connectionString);
        var configuredUser = ValidatePersonalOracleIdentifier(
            connectionBuilder.UserID?.Trim());
        connectionBuilder.Pooling = true;
        connectionBuilder.ConnectionTimeout = 20;

        await using var firstConnection = new OracleConnection(connectionBuilder.ConnectionString);
        await using var secondConnection = new OracleConnection(connectionBuilder.ConnectionString);
        await firstConnection.OpenAsync();
        await secondConnection.OpenAsync();
        var firstSchema = await ValidatePersonalOracleConnectionAsync(
            firstConnection,
            configuredUser);
        var secondSchema = await ValidatePersonalOracleConnectionAsync(
            secondConnection,
            configuredUser);
        if (!firstSchema.Equals(secondSchema, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Both Oracle test connections must use the same personal schema.");
        }

        await EnsureOwnedOracleBaseTablesAsync(firstConnection);
        await EnsureOwnedOracleBaseTablesAsync(secondConnection);
        await SeedOracleRefundFixtureAsync(firstConnection, firstSchema);
        try
        {
            await using var firstTransaction = await firstConnection.BeginTransactionAsync();
            try
            {
                var orderId = await ReadOracleScalarAsync<decimal?>(
                    firstConnection,
                    firstTransaction,
                    $"SELECT ORDER_ID FROM {firstSchema}.T_ORDER " +
                    "WHERE ORDER_ID = :id AND CREATE_BY = :marker FOR UPDATE",
                    new OracleParameter(
                        "id",
                        OracleDbType.Int64,
                        OracleRefundOrderId,
                        ParameterDirection.Input),
                    new OracleParameter(
                        "marker",
                        OracleDbType.Varchar2,
                        OracleRefundMarker,
                        ParameterDirection.Input));
                Assert.Equal(OracleRefundOrderId, Convert.ToInt64(orderId));

                await using var secondTransaction = await secondConnection.BeginTransactionAsync();
                try
                {
                    await using var competingLock = secondConnection.CreateCommand();
                    competingLock.BindByName = true;
                    competingLock.Transaction = (OracleTransaction)secondTransaction;
                    competingLock.CommandText =
                        $"SELECT ORDER_ID FROM {secondSchema}.T_ORDER " +
                        "WHERE ORDER_ID = :id FOR UPDATE NOWAIT";
                    competingLock.Parameters.Add(
                        new OracleParameter(
                            "id",
                            OracleDbType.Int64,
                            OracleRefundOrderId,
                            ParameterDirection.Input));
                    var exception = await Assert.ThrowsAsync<OracleException>(
                        () => competingLock.ExecuteScalarAsync());
                    Assert.Equal(54, exception.Number);
                }
                finally
                {
                    await secondTransaction.RollbackAsync();
                }
            }
            finally
            {
                await firstTransaction.RollbackAsync();
            }

            await using var paymentTransaction = await firstConnection.BeginTransactionAsync();
            try
            {
                await using var paymentQuery = firstConnection.CreateCommand();
                paymentQuery.BindByName = true;
                paymentQuery.Transaction = (OracleTransaction)paymentTransaction;
                paymentQuery.CommandText =
                    $"SELECT PAYMENT_ID, REFUND_AMOUNT FROM {firstSchema}.PAYMENT " +
                    "WHERE PAYMENT_ID = :paymentId AND ORDER_ID = :orderId " +
                    "AND CREATE_BY = :marker AND PAY_STATUS = 'SUCCESS' " +
                    "AND REFUND_AMOUNT + 0.01 <= PAY_AMOUNT FOR UPDATE";
                paymentQuery.Parameters.Add(
                    new OracleParameter(
                        "paymentId",
                        OracleDbType.Int64,
                        OracleRefundPaymentId,
                        ParameterDirection.Input));
                paymentQuery.Parameters.Add(
                    new OracleParameter(
                        "orderId",
                        OracleDbType.Int64,
                        OracleRefundOrderId,
                        ParameterDirection.Input));
                paymentQuery.Parameters.Add(
                    new OracleParameter(
                        "marker",
                        OracleDbType.Varchar2,
                        OracleRefundMarker,
                        ParameterDirection.Input));
                await using var paymentReader = await paymentQuery.ExecuteReaderAsync();
                Assert.True(await paymentReader.ReadAsync());

                var paymentId = Convert.ToInt64(paymentReader.GetValue(0));
                var before = Convert.ToDecimal(paymentReader.GetValue(1));
                await paymentReader.DisposeAsync();
                await using var accumulate = firstConnection.CreateCommand();
                accumulate.BindByName = true;
                accumulate.Transaction = (OracleTransaction)paymentTransaction;
                accumulate.CommandText =
                    $"UPDATE {firstSchema}.PAYMENT " +
                    "SET REFUND_AMOUNT = REFUND_AMOUNT + :amount " +
                    "WHERE PAYMENT_ID = :paymentId";
                accumulate.Parameters.Add(
                    new OracleParameter(
                        "amount",
                        OracleDbType.Decimal,
                        0.01m,
                        ParameterDirection.Input));
                accumulate.Parameters.Add(
                    new OracleParameter(
                        "paymentId",
                        OracleDbType.Int64,
                        paymentId,
                        ParameterDirection.Input));
                Assert.Equal(1, await accumulate.ExecuteNonQueryAsync());
                var after = await ReadOracleScalarAsync<decimal>(
                    firstConnection,
                    paymentTransaction,
                    $"SELECT REFUND_AMOUNT FROM {firstSchema}.PAYMENT " +
                    "WHERE PAYMENT_ID = :paymentId",
                    new OracleParameter(
                        "paymentId",
                        OracleDbType.Int64,
                        paymentId,
                        ParameterDirection.Input));
                Assert.Equal(before + 0.01m, after);
            }
            finally
            {
                await paymentTransaction.RollbackAsync();
            }
        }
        finally
        {
            await CleanupOracleRefundFixtureAsync(firstConnection, firstSchema);
        }
    }

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
        var recoveryProbe = new ApplicationRecoveryReadProbe();
        var interceptor = new CompetingTicketStatusInterceptor(recoveryProbe);
        var recoveryObserver = new ApplicationRecoveryReadObserver(recoveryProbe);
        await using var db = CreateDbContext(connection, interceptor, recoveryObserver);

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
        Assert.Equal(1, recoveryObserver.RefundItemRecoveryReadCount);
        Assert.Empty(db.ChangeTracker.Entries());
        Assert.Null(db.Database.CurrentTransaction);
        await AssertOriginalStateAsync(connection);
    }

    [Fact]
    public async Task CreateAsync_WhenConcurrencyRecoverySelectFails_ReturnsFallbackOnce()
    {
        await using var connection = await OpenConnectionAsync("Data Source=:memory:");
        await SeedIssuedOrderAsync(connection);
        var recovery = new RollbackRecoveryProbe();
        var rollback = new RollbackRecordingInterceptor(recovery);
        var selectFailure = new RecoverySelectFailureInterceptor(
            recovery,
            "ORDER_ITEM");
        var concurrency = new CompetingTicketStatusInterceptor();
        var logger = new RecordingLogger<RefundApplicationService>();
        await using var db = CreateDbContext(
            connection,
            concurrency,
            rollback,
            selectFailure);

        var result = await CreateService(db, logger: logger).CreateAsync(
            7,
            "alice",
            11,
            new CreateRefundRequest([101], "行程变更"),
            CancellationToken.None);

        Assert.Equal(OrderTicketFailure.Conflict, result.Failure);
        Assert.Equal("REFUND_CREATE_CONFLICT", result.ErrorCode);
        Assert.Equal(1, rollback.RollbackCount);
        Assert.Equal(1, selectFailure.AttemptCount);
        Assert.Equal(1, concurrency.SaveAttemptCount);
        Assert.Empty(db.ChangeTracker.Entries());
        Assert.Null(db.Database.CurrentTransaction);
        Assert.Contains(logger.Entries, entry =>
            entry.Level == LogLevel.Warning &&
            entry.Message.Contains("recovery", StringComparison.OrdinalIgnoreCase));
        await AssertOriginalStateAsync(connection);
    }

    [Fact]
    public async Task CreateAsync_WhenConcurrencyRecoveryTokenIsCancelled_ReturnsFallbackOnce()
    {
        await using var connection = await OpenConnectionAsync("Data Source=:memory:");
        await SeedIssuedOrderAsync(connection);
        using var cancellation = new CancellationTokenSource();
        var recovery = new RollbackRecoveryProbe();
        var rollback = new RollbackRecordingInterceptor(recovery, cancellation);
        var concurrency = new CompetingTicketStatusInterceptor();
        var logger = new RecordingLogger<RefundApplicationService>();
        await using var db = CreateDbContext(connection, concurrency, rollback);

        var result = await CreateService(db, logger: logger).CreateAsync(
            7,
            "alice",
            11,
            new CreateRefundRequest([101], "行程变更"),
            cancellation.Token);

        Assert.Equal(OrderTicketFailure.Conflict, result.Failure);
        Assert.Equal("REFUND_CREATE_CONFLICT", result.ErrorCode);
        Assert.True(cancellation.IsCancellationRequested);
        Assert.Equal(1, rollback.RollbackCount);
        Assert.Equal(1, concurrency.SaveAttemptCount);
        Assert.Empty(db.ChangeTracker.Entries());
        Assert.Null(db.Database.CurrentTransaction);
        Assert.Contains(logger.Entries, entry =>
            entry.Level == LogLevel.Warning &&
            entry.Message.Contains("recovery", StringComparison.OrdinalIgnoreCase));
        await AssertOriginalStateAsync(connection);
    }

    [Fact]
    public async Task ApproveAsync_WhenReviewCompletesAfterConcurrencyRollback_ReturnsLatestConflict()
    {
        await using var fixture = await RefundTestData.CreatePendingRefundAsync();
        await MakeSingleItemFinancialsConsistentAsync(fixture);
        var observer = new RefundBulkUpdateObserver();
        var concurrency = new ApproveEntityConcurrencyInterceptor("order-item");
        var reviewedAfterRollback = new ReviewedAfterRollbackInterceptor();
        await using var db = fixture.CreateDbContext(
            observer,
            concurrency,
            reviewedAfterRollback);
        var service = CreateReviewService(db, fixture.TimeProvider);

        var result = await service.ApproveAsync(
            "admin",
            fixture.RefundId,
            new ApproveRefundRequest(null),
            CancellationToken.None);

        Assert.Equal(OrderTicketFailure.Conflict, result.Failure);
        Assert.Equal("REFUND_ALREADY_REVIEWED", result.ErrorCode);
        Assert.Equal(1, observer.PaymentUpdateRows);
        Assert.Equal(1, observer.ReservationUpdateRows);
        Assert.Equal(1, reviewedAfterRollback.MutationAffectedRows);
        Assert.Empty(db.ChangeTracker.Entries());
        Assert.Equal(0m, await fixture.PaymentRefundAmountAsync());
        Assert.Equal("ACTIVE", await fixture.ReservationStatusAsync());
        Assert.Equal("REFUNDING", await fixture.ItemStatusAsync());
        Assert.Equal("REFUNDING", await fixture.TicketStatusAsync());
        Assert.Equal("ISSUED", await fixture.OrderStatusAsync());
        var latest = await fixture.Db.Set<RefundRequest>()
            .AsNoTracking()
            .SingleAsync(item => item.RefundId == fixture.RefundId);
        Assert.Equal("APPROVED", latest.ApproveStatus);
        Assert.Equal("COMPLETED", latest.RefundStatus);
    }

    [Fact]
    public async Task ApproveAsync_WhenConcurrencyRecoverySelectFails_ReturnsFallbackOnce()
    {
        await using var fixture = await RefundTestData.CreatePendingRefundAsync();
        await MakeSingleItemFinancialsConsistentAsync(fixture);
        var recovery = new RollbackRecoveryProbe();
        var rollback = new RollbackRecordingInterceptor(recovery);
        var selectFailure = new RecoverySelectFailureInterceptor(
            recovery,
            "REFUND_REQUEST");
        var observer = new RefundBulkUpdateObserver();
        var concurrency = new ApproveEntityConcurrencyInterceptor("order-item");
        var logger = new RecordingLogger<RefundReviewService>();
        await using var db = fixture.CreateDbContext(
            observer,
            concurrency,
            rollback,
            selectFailure);

        var result = await CreateReviewService(db, fixture.TimeProvider, logger)
            .ApproveAsync(
                "admin",
                fixture.RefundId,
                new ApproveRefundRequest(null),
                CancellationToken.None);

        Assert.Equal(OrderTicketFailure.Conflict, result.Failure);
        Assert.Equal("REFUND_REVIEW_CONFLICT", result.ErrorCode);
        Assert.Equal(1, rollback.RollbackCount);
        Assert.Equal(1, selectFailure.AttemptCount);
        Assert.Equal(1, concurrency.SaveAttemptCount);
        Assert.Equal(1, observer.PaymentUpdateAttempts);
        Assert.Equal(1, observer.ReservationUpdateAttempts);
        Assert.Empty(db.ChangeTracker.Entries());
        Assert.Null(db.Database.CurrentTransaction);
        Assert.Contains(logger.Entries, entry =>
            entry.Level == LogLevel.Warning &&
            entry.Message.Contains("recovery", StringComparison.OrdinalIgnoreCase));
        await AssertPendingApprovalStateAsync(fixture);
    }

    [Fact]
    public async Task ApproveAsync_WhenConcurrencyRecoveryTokenIsCancelled_ReturnsFallbackOnce()
    {
        await using var fixture = await RefundTestData.CreatePendingRefundAsync();
        await MakeSingleItemFinancialsConsistentAsync(fixture);
        using var cancellation = new CancellationTokenSource();
        var recovery = new RollbackRecoveryProbe();
        var rollback = new RollbackRecordingInterceptor(recovery, cancellation);
        var observer = new RefundBulkUpdateObserver();
        var concurrency = new ApproveEntityConcurrencyInterceptor("order-item");
        var logger = new RecordingLogger<RefundReviewService>();
        await using var db = fixture.CreateDbContext(observer, concurrency, rollback);

        var result = await CreateReviewService(db, fixture.TimeProvider, logger)
            .ApproveAsync(
                "admin",
                fixture.RefundId,
                new ApproveRefundRequest(null),
                cancellation.Token);

        Assert.Equal(OrderTicketFailure.Conflict, result.Failure);
        Assert.Equal("REFUND_REVIEW_CONFLICT", result.ErrorCode);
        Assert.True(cancellation.IsCancellationRequested);
        Assert.Equal(1, rollback.RollbackCount);
        Assert.Equal(1, concurrency.SaveAttemptCount);
        Assert.Equal(1, observer.PaymentUpdateAttempts);
        Assert.Equal(1, observer.ReservationUpdateAttempts);
        Assert.Empty(db.ChangeTracker.Entries());
        Assert.Null(db.Database.CurrentTransaction);
        Assert.Contains(logger.Entries, entry =>
            entry.Level == LogLevel.Warning &&
            entry.Message.Contains("recovery", StringComparison.OrdinalIgnoreCase));
        await AssertPendingApprovalStateAsync(fixture);
    }

    [Fact]
    public async Task RejectAsync_WhenConcurrencyRecoverySelectFails_ReturnsFallbackOnce()
    {
        await using var fixture = await RefundTestData.CreatePendingRefundAsync();
        var recovery = new RollbackRecoveryProbe();
        var rollback = new RollbackRecordingInterceptor(recovery);
        var selectFailure = new RecoverySelectFailureInterceptor(
            recovery,
            "REFUND_REQUEST");
        var concurrency = new ApproveEntityConcurrencyInterceptor("order-item");
        var logger = new RecordingLogger<RefundReviewService>();
        await using var db = fixture.CreateDbContext(
            concurrency,
            rollback,
            selectFailure);

        var result = await CreateReviewService(db, fixture.TimeProvider, logger)
            .RejectAsync(
                "admin",
                fixture.RefundId,
                new RejectRefundRequest("拒绝"),
                CancellationToken.None);

        Assert.Equal(OrderTicketFailure.Conflict, result.Failure);
        Assert.Equal("REFUND_REVIEW_CONFLICT", result.ErrorCode);
        Assert.Equal(1, rollback.RollbackCount);
        Assert.Equal(1, selectFailure.AttemptCount);
        Assert.Equal(1, concurrency.SaveAttemptCount);
        Assert.Empty(db.ChangeTracker.Entries());
        Assert.Null(db.Database.CurrentTransaction);
        Assert.Contains(logger.Entries, entry =>
            entry.Level == LogLevel.Warning &&
            entry.Message.Contains("recovery", StringComparison.OrdinalIgnoreCase));
        await AssertPendingApprovalStateAsync(fixture);
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
        var recoveryProbe = new ApplicationRecoveryReadProbe();
        var interceptor = new ThrowingSaveInterceptor(
            () => new DbUpdateException(
                "Save failed.",
                new InvalidOperationException(
                    "ORA-00001: unique constraint (APP_OWNER.UK_REFUND_ORDER_ITEM) violated")),
            recoveryProbe.MarkMutation);
        var recoveryObserver = new ApplicationRecoveryReadObserver(recoveryProbe);
        await using var db = CreateDbContext(connection, interceptor, recoveryObserver);

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
        Assert.Equal(1, recoveryObserver.RefundItemRecoveryReadCount);
        Assert.Empty(db.ChangeTracker.Entries());
        await AssertOriginalStateAsync(connection);
    }

    [Fact]
    public async Task CreateAsync_WhenOrderItemConstraintRecoverySelectFails_ReturnsDuplicateOnce()
    {
        await using var connection = await OpenConnectionAsync("Data Source=:memory:");
        await SeedIssuedOrderAsync(connection);
        var recovery = new RollbackRecoveryProbe();
        var rollback = new RollbackRecordingInterceptor(recovery);
        var selectFailure = new RecoverySelectFailureInterceptor(
            recovery,
            "ORDER_ITEM");
        var save = new ThrowingSaveInterceptor(
            () => new DbUpdateException(
                "Save failed.",
                new InvalidOperationException(
                    "ORA-00001: unique constraint (APP_OWNER.UK_REFUND_ORDER_ITEM) violated")));
        var logger = new RecordingLogger<RefundApplicationService>();
        await using var db = CreateDbContext(connection, save, rollback, selectFailure);

        var result = await CreateService(db, logger: logger).CreateAsync(
            7,
            "alice",
            11,
            new CreateRefundRequest([101], "行程变更"),
            CancellationToken.None);

        Assert.Equal(OrderTicketFailure.Conflict, result.Failure);
        Assert.Equal("REFUND_ITEM_ALREADY_REQUESTED", result.ErrorCode);
        Assert.Equal(1, save.CallCount);
        Assert.Equal(1, rollback.RollbackCount);
        Assert.Equal(1, selectFailure.AttemptCount);
        Assert.Empty(db.ChangeTracker.Entries());
        Assert.Null(db.Database.CurrentTransaction);
        Assert.Contains(logger.Entries, entry =>
            entry.Level == LogLevel.Warning &&
            entry.Message.Contains("recovery", StringComparison.OrdinalIgnoreCase));
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

    [Fact]
    public async Task ApproveAsync_WhenReservationReleaseCountChanges_RollsBackExecutedPaymentUpdate()
    {
        await using var fixture = await RefundTestData.CreatePendingRefundAsync();
        await MakeSingleItemFinancialsConsistentAsync(fixture);
        await fixture.Db.Database.ExecuteSqlRawAsync(
            """
            CREATE TRIGGER DELETE_RESERVATION_AFTER_PAYMENT_UPDATE
            AFTER UPDATE OF REFUND_AMOUNT ON PAYMENT
            BEGIN
                DELETE FROM SEAT_RESERVATION WHERE ORDER_ITEM_ID = 101;
            END;
            """);
        var interceptor = new RefundBulkUpdateObserver();
        await using var db = fixture.CreateDbContext(interceptor);
        var service = CreateReviewService(db, fixture.TimeProvider);

        var result = await service.ApproveAsync(
            "admin",
            fixture.RefundId,
            new ApproveRefundRequest(null),
            CancellationToken.None);

        Assert.Equal(OrderTicketFailure.Conflict, result.Failure);
        Assert.Equal("REFUND_RESERVATION_DATA_INCONSISTENT", result.ErrorCode);
        Assert.Equal(1, interceptor.PaymentUpdateRows);
        Assert.Equal(0, interceptor.TrackedPaymentsAtUpdate);
        Assert.Equal(0, interceptor.TrackedReservationsAtUpdate);
        Assert.Equal(1, interceptor.RefundRequestRecoveryReadCount);
        Assert.Empty(db.ChangeTracker.Entries());
        Assert.Equal(0m, await fixture.PaymentRefundAmountAsync());
        Assert.Equal("ACTIVE", await fixture.ReservationStatusAsync());
        Assert.Equal("REFUNDING", await fixture.ItemStatusAsync());
        Assert.Equal("REFUNDING", await fixture.TicketStatusAsync());
        Assert.Equal("PENDING", await fixture.RefundApproveStatusAsync());
    }

    [Fact]
    public async Task ApproveAsync_WhenReservationConflictRecoverySelectFails_ReturnsFallbackOnce()
    {
        await using var fixture = await RefundTestData.CreatePendingRefundAsync();
        await MakeSingleItemFinancialsConsistentAsync(fixture);
        await fixture.Db.Database.ExecuteSqlRawAsync(
            """
            CREATE TRIGGER DELETE_RESERVATION_AFTER_PAYMENT_UPDATE_RECOVERY
            AFTER UPDATE OF REFUND_AMOUNT ON PAYMENT
            BEGIN
                DELETE FROM SEAT_RESERVATION WHERE ORDER_ITEM_ID = 101;
            END;
            """);
        var recovery = new RollbackRecoveryProbe();
        var rollback = new RollbackRecordingInterceptor(recovery);
        var selectFailure = new RecoverySelectFailureInterceptor(
            recovery,
            "REFUND_REQUEST");
        var observer = new RefundBulkUpdateObserver();
        var logger = new RecordingLogger<RefundReviewService>();
        await using var db = fixture.CreateDbContext(
            observer,
            rollback,
            selectFailure);

        var result = await CreateReviewService(db, fixture.TimeProvider, logger)
            .ApproveAsync(
                "admin",
                fixture.RefundId,
                new ApproveRefundRequest(null),
                CancellationToken.None);

        Assert.Equal(OrderTicketFailure.Conflict, result.Failure);
        Assert.Equal("REFUND_RESERVATION_DATA_INCONSISTENT", result.ErrorCode);
        Assert.Equal(1, rollback.RollbackCount);
        Assert.Equal(1, selectFailure.AttemptCount);
        Assert.Equal(1, observer.PaymentUpdateAttempts);
        Assert.Equal(1, observer.ReservationUpdateAttempts);
        Assert.Equal(1, observer.PaymentUpdateRows);
        Assert.Equal(0, observer.ReservationUpdateRows);
        Assert.Empty(db.ChangeTracker.Entries());
        Assert.Null(db.Database.CurrentTransaction);
        Assert.Contains(logger.Entries, entry =>
            entry.Level == LogLevel.Warning &&
            entry.Message.Contains("recovery", StringComparison.OrdinalIgnoreCase));
        await AssertPendingApprovalStateAsync(fixture);
    }

    [Fact]
    public async Task ApproveAsync_WhenPaymentUpdateAffectsNoRows_RequeriesAfterRollback()
    {
        await using var fixture = await RefundTestData.CreatePendingRefundAsync();
        await MakeSingleItemFinancialsConsistentAsync(fixture);
        var payment = await fixture.Db.Set<Payment>().SingleAsync();
        payment.RefundAmount = 22m;
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();
        var observer = new RefundBulkUpdateObserver();
        await using var db = fixture.CreateDbContext(observer);
        var service = CreateReviewService(db, fixture.TimeProvider);

        var result = await service.ApproveAsync(
            "admin",
            fixture.RefundId,
            new ApproveRefundRequest(null),
            CancellationToken.None);

        Assert.Equal(OrderTicketFailure.Conflict, result.Failure);
        Assert.Equal("REFUND_PAYMENT_AMOUNT_CONFLICT", result.ErrorCode);
        Assert.Equal(1, observer.PaymentUpdateAttempts);
        Assert.Equal(0, observer.PaymentUpdateRows);
        Assert.Equal(0, observer.ReservationUpdateRows);
        Assert.Equal(1, observer.RefundRequestRecoveryReadCount);
        Assert.Empty(db.ChangeTracker.Entries());
        Assert.Equal(22m, await fixture.PaymentRefundAmountAsync());
        Assert.Equal("ACTIVE", await fixture.ReservationStatusAsync());
        Assert.Equal("REFUNDING", await fixture.ItemStatusAsync());
        Assert.Equal("REFUNDING", await fixture.TicketStatusAsync());
        Assert.Equal("ISSUED", await fixture.OrderStatusAsync());
        Assert.Equal("PENDING", await fixture.RefundApproveStatusAsync());
    }

    [Fact]
    public async Task ApproveAsync_WhenPaymentConflictRecoverySelectFails_ReturnsFallbackOnce()
    {
        await using var fixture = await RefundTestData.CreatePendingRefundAsync();
        await MakeSingleItemFinancialsConsistentAsync(fixture);
        var payment = await fixture.Db.Set<Payment>().SingleAsync();
        payment.RefundAmount = 22m;
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();
        var recovery = new RollbackRecoveryProbe();
        var rollback = new RollbackRecordingInterceptor(recovery);
        var selectFailure = new RecoverySelectFailureInterceptor(
            recovery,
            "REFUND_REQUEST");
        var observer = new RefundBulkUpdateObserver();
        var logger = new RecordingLogger<RefundReviewService>();
        await using var db = fixture.CreateDbContext(
            observer,
            rollback,
            selectFailure);

        var result = await CreateReviewService(db, fixture.TimeProvider, logger)
            .ApproveAsync(
                "admin",
                fixture.RefundId,
                new ApproveRefundRequest(null),
                CancellationToken.None);

        Assert.Equal(OrderTicketFailure.Conflict, result.Failure);
        Assert.Equal("REFUND_PAYMENT_AMOUNT_CONFLICT", result.ErrorCode);
        Assert.Equal(1, rollback.RollbackCount);
        Assert.Equal(1, selectFailure.AttemptCount);
        Assert.Equal(1, observer.PaymentUpdateAttempts);
        Assert.Equal(0, observer.PaymentUpdateRows);
        Assert.Equal(0, observer.ReservationUpdateAttempts);
        Assert.Empty(db.ChangeTracker.Entries());
        Assert.Null(db.Database.CurrentTransaction);
        Assert.Contains(logger.Entries, entry =>
            entry.Level == LogLevel.Warning &&
            entry.Message.Contains("recovery", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(22m, await fixture.PaymentRefundAmountAsync());
        Assert.Equal("ACTIVE", await fixture.ReservationStatusAsync());
        Assert.Equal("REFUNDING", await fixture.ItemStatusAsync());
        Assert.Equal("REFUNDING", await fixture.TicketStatusAsync());
        Assert.Equal("ISSUED", await fixture.OrderStatusAsync());
        Assert.Equal("PENDING", await fixture.RefundApproveStatusAsync());
    }

    [Fact]
    public async Task ApproveAsync_WhenSaveFails_RollsBackBulkUpdatesAndClearsTracker()
    {
        await using var fixture = await RefundTestData.CreatePendingRefundAsync();
        await MakeSingleItemFinancialsConsistentAsync(fixture);
        await fixture.Db.Database.ExecuteSqlRawAsync(
            """
            CREATE TRIGGER FAIL_REFUND_APPROVE
            BEFORE UPDATE ON REFUND_REQUEST
            BEGIN
                SELECT RAISE(ABORT, 'forced approval failure');
            END;
            """);
        var interceptor = new RefundBulkUpdateObserver();
        await using var db = fixture.CreateDbContext(interceptor);
        var service = CreateReviewService(db, fixture.TimeProvider);

        var result = await service.ApproveAsync(
            "admin",
            fixture.RefundId,
            new ApproveRefundRequest(null),
            CancellationToken.None);

        Assert.Equal(OrderTicketFailure.Internal, result.Failure);
        Assert.Equal("REFUND_APPROVE_FAILED", result.ErrorCode);
        Assert.Equal(1, interceptor.PaymentUpdateRows);
        Assert.Equal(1, interceptor.ReservationUpdateRows);
        Assert.Empty(db.ChangeTracker.Entries());
        Assert.Null(db.Database.CurrentTransaction);
        Assert.Equal(0m, await fixture.PaymentRefundAmountAsync());
        Assert.Equal("ACTIVE", await fixture.ReservationStatusAsync());
        Assert.Equal("REFUNDING", await fixture.ItemStatusAsync());
        Assert.Equal("REFUNDING", await fixture.TicketStatusAsync());
        Assert.Equal("PENDING", await fixture.RefundApproveStatusAsync());
    }

    [Fact]
    public async Task ApproveAsync_WhenRequestTokensChangeBeforeSave_RollsBackBulkUpdates()
    {
        await using var fixture = await RefundTestData.CreatePendingRefundAsync();
        await MakeSingleItemFinancialsConsistentAsync(fixture);
        var interceptor = new ApproveRequestConcurrencyInterceptor();
        await using var db = fixture.CreateDbContext(interceptor);
        var service = CreateReviewService(db, fixture.TimeProvider);

        var result = await service.ApproveAsync(
            "admin",
            fixture.RefundId,
            new ApproveRefundRequest(null),
            CancellationToken.None);

        Assert.Equal(OrderTicketFailure.Conflict, result.Failure);
        Assert.Equal("REFUND_REVIEW_CONFLICT", result.ErrorCode);
        Assert.True(interceptor.Mutated);
        Assert.Equal(84m, interceptor.ObservedRefundAmountBeforeMutation);
        Assert.Equal("RELEASED", interceptor.ObservedReservationStatusBeforeMutation);
        Assert.Empty(db.ChangeTracker.Entries());
        Assert.Null(db.Database.CurrentTransaction);
        Assert.Equal(0m, await fixture.PaymentRefundAmountAsync());
        Assert.Equal("ACTIVE", await fixture.ReservationStatusAsync());
        Assert.Equal("REFUNDING", await fixture.ItemStatusAsync());
        Assert.Equal("PENDING", await fixture.RefundApproveStatusAsync());
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

    private static string ValidatePersonalOracleIdentifier(string? identifier)
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
                "Oracle refund concurrency tests require a safe, explicit personal test schema and refuse APP_OWNER or DEPLOY_USER.");
        }

        return identifier;
    }

    private static bool IsAsciiLetter(char character) =>
        character is >= 'A' and <= 'Z' or >= 'a' and <= 'z';

    private static async Task SeedOracleRefundFixtureAsync(
        OracleConnection connection,
        string schema)
    {
        await CleanupOracleRefundFixtureAsync(connection, schema);
        var showId = await ReadOracleScalarAsync<decimal?>(
            connection,
            null,
            $"SELECT MIN(SHOW_ID) FROM {schema}.SHOW");
        var seatMapId = await ReadOracleScalarAsync<decimal?>(
            connection,
            null,
            $"SELECT MIN(SEAT_MAP_ID) FROM {schema}.SEAT_MAP");
        if (!showId.HasValue || !seatMapId.HasValue)
        {
            throw new InvalidOperationException(
                "Oracle refund concurrency tests require at least one owned SHOW " +
                "and SEAT_MAP row for read-only foreign-key anchors.");
        }

        try
        {
            await ExecuteOracleNonQueryAsync(
                connection,
                $"INSERT INTO {schema}.SYS_USER (" +
                "USER_ID, USER_NAME, PASSWORD_HASH, PHONE, USER_TYPE, STATUS, " +
                "CREATE_BY, UPDATE_BY) VALUES (:userId, :userName, :passwordHash, " +
                ":phone, 'NORMAL', 1, :marker, :marker)",
                new OracleParameter("userId", OracleDbType.Int64, OracleRefundUserId,
                    ParameterDirection.Input),
                new OracleParameter("userName", OracleDbType.Varchar2,
                    "oracle_refund_gate_998830001", ParameterDirection.Input),
                new OracleParameter("passwordHash", OracleDbType.Varchar2,
                    "oracle-refund-gate-not-a-real-password", ParameterDirection.Input),
                new OracleParameter("phone", OracleDbType.Varchar2,
                    "998830001", ParameterDirection.Input),
                new OracleParameter("marker", OracleDbType.Varchar2,
                    OracleRefundMarker, ParameterDirection.Input));
            await ExecuteOracleNonQueryAsync(
                connection,
                $"INSERT INTO {schema}.SHOW_SESSION (" +
                "SESSION_ID, SHOW_ID, SEAT_MAP_ID, START_TIME, END_TIME, " +
                "SALE_START_TIME, SALE_END_TIME, SESSION_STATUS, CREATE_BY, UPDATE_BY) " +
                "VALUES (:sessionId, :showId, :seatMapId, " +
                "SYSTIMESTAMP + INTERVAL '7' DAY, " +
                "SYSTIMESTAMP + INTERVAL '7' DAY + INTERVAL '2' HOUR, " +
                "SYSTIMESTAMP - INTERVAL '1' DAY, " +
                "SYSTIMESTAMP + INTERVAL '6' DAY, 'ONSALE', :marker, :marker)",
                new OracleParameter("sessionId", OracleDbType.Int64,
                    OracleRefundSessionId, ParameterDirection.Input),
                new OracleParameter("showId", OracleDbType.Int64,
                    Convert.ToInt64(showId.Value), ParameterDirection.Input),
                new OracleParameter("seatMapId", OracleDbType.Int64,
                    Convert.ToInt64(seatMapId.Value), ParameterDirection.Input),
                new OracleParameter("marker", OracleDbType.Varchar2,
                    OracleRefundMarker, ParameterDirection.Input));
            await ExecuteOracleNonQueryAsync(
                connection,
                $"INSERT INTO {schema}.T_ORDER (" +
                "ORDER_ID, ORDER_NO, USER_ID, SESSION_ID, ORDER_TYPE, TOTAL_AMOUNT, " +
                "DISCOUNT_AMOUNT, TICKET_COUNT, ORDER_STATUS, " +
                "EXPIRE_TIME, PAY_TIME, ISSUE_TIME, SOURCE, IDEMPOTENCY_KEY, " +
                "IDEMPOTENCY_REQUEST_HASH, CREATE_BY, UPDATE_BY) " +
                "VALUES (:orderId, :orderNo, :userId, :sessionId, 'NORMAL', 100, " +
                "0, 1, 'ISSUED', SYSTIMESTAMP + INTERVAL '1' DAY, " +
                "SYSTIMESTAMP, SYSTIMESTAMP, 'WEB', :idempotencyKey, " +
                ":requestHash, :marker, :marker)",
                new OracleParameter("orderId", OracleDbType.Int64, OracleRefundOrderId,
                    ParameterDirection.Input),
                new OracleParameter("orderNo", OracleDbType.Varchar2,
                    "ORAREFUNDGATE998830001", ParameterDirection.Input),
                new OracleParameter("userId", OracleDbType.Int64,
                    OracleRefundUserId, ParameterDirection.Input),
                new OracleParameter("sessionId", OracleDbType.Int64,
                    OracleRefundSessionId, ParameterDirection.Input),
                new OracleParameter("idempotencyKey", OracleDbType.Varchar2,
                    "oracle-refund-lock-gate", ParameterDirection.Input),
                new OracleParameter("requestHash", OracleDbType.Char,
                    new string('A', 64), ParameterDirection.Input),
                new OracleParameter("marker", OracleDbType.Varchar2,
                    OracleRefundMarker, ParameterDirection.Input));
            await ExecuteOracleNonQueryAsync(
                connection,
                $"INSERT INTO {schema}.PAYMENT (" +
                "PAYMENT_ID, PAYMENT_NO, ORDER_ID, USER_ID, PAY_AMOUNT, PAY_CHANNEL, " +
                "PAY_STATUS, PAY_TIME, REFUND_AMOUNT, CREATE_BY, UPDATE_BY) VALUES (" +
                ":paymentId, :paymentNo, :orderId, :userId, 100, 'ALIPAY', " +
                "'SUCCESS', SYSTIMESTAMP, 0, :marker, :marker)",
                new OracleParameter("paymentId", OracleDbType.Int64,
                    OracleRefundPaymentId, ParameterDirection.Input),
                new OracleParameter("paymentNo", OracleDbType.Varchar2,
                    "ORAREFUNDGATEPAY998830001", ParameterDirection.Input),
                new OracleParameter("orderId", OracleDbType.Int64, OracleRefundOrderId,
                    ParameterDirection.Input),
                new OracleParameter("userId", OracleDbType.Int64,
                    OracleRefundUserId, ParameterDirection.Input),
                new OracleParameter("marker", OracleDbType.Varchar2,
                    OracleRefundMarker, ParameterDirection.Input));
            await ExecuteOracleNonQueryAsync(connection, "COMMIT");
        }
        catch
        {
            await ExecuteOracleNonQueryAsync(connection, "ROLLBACK");
            await CleanupOracleRefundFixtureAsync(connection, schema);
            throw;
        }
    }

    private static async Task CleanupOracleRefundFixtureAsync(
        OracleConnection connection,
        string schema)
    {
        await ExecuteOracleNonQueryAsync(connection, "ROLLBACK");
        await ExecuteOracleNonQueryAsync(
            connection,
            $"DELETE FROM {schema}.PAYMENT WHERE PAYMENT_ID = :id " +
            "AND CREATE_BY = :marker",
            new OracleParameter("id", OracleDbType.Int64, OracleRefundPaymentId,
                ParameterDirection.Input),
            new OracleParameter("marker", OracleDbType.Varchar2, OracleRefundMarker,
                ParameterDirection.Input));
        await ExecuteOracleNonQueryAsync(
            connection,
            $"DELETE FROM {schema}.T_ORDER WHERE ORDER_ID = :id " +
            "AND CREATE_BY = :marker",
            new OracleParameter("id", OracleDbType.Int64, OracleRefundOrderId,
                ParameterDirection.Input),
            new OracleParameter("marker", OracleDbType.Varchar2, OracleRefundMarker,
                ParameterDirection.Input));
        await ExecuteOracleNonQueryAsync(
            connection,
            $"DELETE FROM {schema}.SHOW_SESSION WHERE SESSION_ID = :id " +
            "AND CREATE_BY = :marker",
            new OracleParameter("id", OracleDbType.Int64, OracleRefundSessionId,
                ParameterDirection.Input),
            new OracleParameter("marker", OracleDbType.Varchar2, OracleRefundMarker,
                ParameterDirection.Input));
        await ExecuteOracleNonQueryAsync(
            connection,
            $"DELETE FROM {schema}.SYS_USER WHERE USER_ID = :id " +
            "AND CREATE_BY = :marker",
            new OracleParameter("id", OracleDbType.Int64, OracleRefundUserId,
                ParameterDirection.Input),
            new OracleParameter("marker", OracleDbType.Varchar2, OracleRefundMarker,
                ParameterDirection.Input));
        await ExecuteOracleNonQueryAsync(connection, "COMMIT");
    }

    private static async Task ExecuteOracleNonQueryAsync(
        OracleConnection connection,
        string commandText,
        params OracleParameter[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.BindByName = true;
        command.CommandText = commandText;
        command.Parameters.AddRange(parameters);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<string> ValidatePersonalOracleConnectionAsync(
        OracleConnection connection,
        string configuredUser)
    {
        var sessionUser = ValidatePersonalOracleIdentifier(
            await ReadOracleScalarAsync<string>(
                connection,
                null,
                "SELECT SYS_CONTEXT('USERENV', 'SESSION_USER') FROM DUAL"));
        var currentSchema = ValidatePersonalOracleIdentifier(
            await ReadOracleScalarAsync<string>(
                connection,
                null,
                "SELECT SYS_CONTEXT('USERENV', 'CURRENT_SCHEMA') FROM DUAL"));
        if (!configuredUser.Equals(sessionUser, StringComparison.OrdinalIgnoreCase) ||
            !configuredUser.Equals(currentSchema, StringComparison.OrdinalIgnoreCase) ||
            !sessionUser.Equals(sessionUser.ToUpperInvariant(), StringComparison.Ordinal) ||
            !currentSchema.Equals(currentSchema.ToUpperInvariant(), StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The Oracle connection must log in as, and remain in, the configured unquoted personal test schema.");
        }

        return currentSchema;
    }

    private static async Task EnsureOwnedOracleBaseTablesAsync(
        OracleConnection connection)
    {
        var ownedTableCount = await ReadOracleScalarAsync<decimal>(
            connection,
            null,
            "SELECT COUNT(*) FROM USER_TABLES " +
            "WHERE TABLE_NAME IN (:orderTable, :paymentTable, :userTable, " +
            ":sessionTable, :showTable, :seatMapTable)",
            new OracleParameter(
                "orderTable",
                OracleDbType.Varchar2,
                "T_ORDER",
                System.Data.ParameterDirection.Input),
            new OracleParameter(
                "paymentTable",
                OracleDbType.Varchar2,
                "PAYMENT",
                ParameterDirection.Input),
            new OracleParameter(
                "userTable",
                OracleDbType.Varchar2,
                "SYS_USER",
                ParameterDirection.Input),
            new OracleParameter(
                "sessionTable",
                OracleDbType.Varchar2,
                "SHOW_SESSION",
                ParameterDirection.Input),
            new OracleParameter(
                "showTable",
                OracleDbType.Varchar2,
                "SHOW",
                ParameterDirection.Input),
            new OracleParameter(
                "seatMapTable",
                OracleDbType.Varchar2,
                "SEAT_MAP",
                System.Data.ParameterDirection.Input));
        if (ownedTableCount != 6m)
        {
            throw new InvalidOperationException(
                "T_ORDER, PAYMENT, SYS_USER, SHOW_SESSION, SHOW, and SEAT_MAP " +
                "must be base tables " +
                "owned by the personal Oracle test user; synonyms and shared-owner " +
                "tables are refused.");
        }
    }

    private static async Task<T> ReadOracleScalarAsync<T>(
        OracleConnection connection,
        DbTransaction? transaction,
        string commandText,
        params OracleParameter[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.BindByName = true;
        command.Transaction = (OracleTransaction?)transaction;
        command.CommandText = commandText;
        command.Parameters.AddRange(parameters);
        var value = await command.ExecuteScalarAsync();
        if (value is null or DBNull)
        {
            return default!;
        }

        var targetType = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
        return (T)Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture);
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
        IRefundLockCoordinator? lockCoordinator = null,
        ILogger<RefundApplicationService>? logger = null) => new(
        db,
        new RefundPolicyEngine(),
        new FixedTimeProvider(RefundTestData.FixedUtcNow),
        lockCoordinator ?? new TestRefundLockCoordinator(db),
        logger ?? NullLogger<RefundApplicationService>.Instance,
        new NullOrderTicketAuditSink());

    private static RefundReviewService CreateReviewService(
        AppDbContext db,
        TimeProvider timeProvider,
        ILogger<RefundReviewService>? logger = null) => new(
        db,
        timeProvider,
        new TestRefundLockCoordinator(db),
        logger ?? NullLogger<RefundReviewService>.Instance,
        new NullOrderTicketAuditSink());

    private static async Task MakeSingleItemFinancialsConsistentAsync(
        RefundTestData fixture)
    {
        var itemTotal = await fixture.Db.Set<OrderItem>()
            .SumAsync(item => item.UnitPrice);
        var order = await fixture.Db.Set<Order>().SingleAsync();
        var payment = await fixture.Db.Set<Payment>().SingleAsync();
        order.TotalAmount = itemTotal;
        order.DiscountAmount = 0m;
        payment.PayAmount = itemTotal;
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();
    }

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

    private static async Task AssertPendingApprovalStateAsync(RefundTestData fixture)
    {
        Assert.Equal(0m, await fixture.PaymentRefundAmountAsync());
        Assert.Equal("ACTIVE", await fixture.ReservationStatusAsync());
        Assert.Equal("REFUNDING", await fixture.ItemStatusAsync());
        Assert.Equal("REFUNDING", await fixture.TicketStatusAsync());
        Assert.Equal("ISSUED", await fixture.OrderStatusAsync());
        Assert.Equal("PENDING", await fixture.RefundApproveStatusAsync());
        Assert.Equal("PENDING", await fixture.Db.Set<RefundRequest>()
            .AsNoTracking()
            .Where(item => item.RefundId == fixture.RefundId)
            .Select(item => item.RefundStatus)
            .SingleAsync());
    }

    private static void AssertOracleLockCommand(
        RecordedOracleLockCommand command,
        string expectedTable,
        long expectedId,
        DbConnection expectedConnection,
        DbTransaction expectedTransaction)
    {
        var normalizedSql = string.Join(
            ' ',
            command.CommandText.Split(
                (char[]?)null,
                StringSplitOptions.RemoveEmptyEntries));
        var expectedColumn = expectedTable.EndsWith(
            ".REFUND_REQUEST",
            StringComparison.Ordinal)
            ? "REFUND_ID"
            : "ORDER_ID";
        Assert.Equal(
            $"SELECT {expectedColumn} FROM {expectedTable} " +
            $"WHERE {expectedColumn} = :id FOR UPDATE",
            normalizedSql);
        Assert.Same(expectedConnection, command.Connection);
        Assert.Same(expectedTransaction, command.Transaction);
        Assert.Equal("id", command.ParameterName);
        Assert.Equal(DbType.Int64, command.ParameterType);
        Assert.Equal(expectedId, command.ParameterValue);
    }

    private sealed class OracleRefundFactAttribute : FactAttribute
    {
        public OracleRefundFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(
                    "SHOWTIME_ORACLE_REFUND_TEST_CONNECTION")))
            {
                Skip =
                    "SHOWTIME_ORACLE_REFUND_TEST_CONNECTION is not configured; no Oracle connection will be opened.";
            }
        }
    }

    private sealed class RollbackRecoveryProbe
    {
        private int rollbackCompleted;

        public bool RollbackCompleted => Volatile.Read(ref rollbackCompleted) == 1;

        public void MarkRollbackCompleted() => Volatile.Write(ref rollbackCompleted, 1);
    }

    private sealed class RollbackRecordingInterceptor(
        RollbackRecoveryProbe probe,
        CancellationTokenSource? cancellation = null) : DbTransactionInterceptor
    {
        public int RollbackCount { get; private set; }

        public override Task TransactionRolledBackAsync(
            DbTransaction transaction,
            TransactionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            RollbackCount++;
            probe.MarkRollbackCompleted();
            cancellation?.Cancel();
            return Task.CompletedTask;
        }
    }

    private sealed class RecoverySelectFailureInterceptor(
        RollbackRecoveryProbe probe,
        string tableName) : DbCommandInterceptor
    {
        public int AttemptCount { get; private set; }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (!probe.RollbackCompleted ||
                !command.CommandText.TrimStart().StartsWith(
                    "SELECT",
                    StringComparison.OrdinalIgnoreCase) ||
                !command.CommandText.Contains(
                    $"\"{tableName}\"",
                    StringComparison.OrdinalIgnoreCase))
            {
                return ValueTask.FromResult(result);
            }

            AttemptCount++;
            return ValueTask.FromException<InterceptionResult<DbDataReader>>(
                new RecoveryReadDbException());
        }
    }

    private sealed class RecoveryReadDbException()
        : DbException("Simulated transient recovery SELECT failure.");

    private sealed record RecordedLog(
        LogLevel Level,
        string Message,
        Exception? Exception);

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<RecordedLog> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) => Entries.Add(
            new RecordedLog(logLevel, formatter(state, exception), exception));
    }

    private sealed class ThrowingSaveInterceptor(
        Func<Exception> exceptionFactory,
        Action? beforeThrow = null) : SaveChangesInterceptor
    {
        public int CallCount { get; private set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            beforeThrow?.Invoke();
            return ValueTask.FromException<InterceptionResult<int>>(
                exceptionFactory());
        }
    }

    private sealed class CompetingTicketStatusInterceptor(
        ApplicationRecoveryReadProbe? recoveryProbe = null) : SaveChangesInterceptor
    {
        private int mutated;

        public bool Mutated => Volatile.Read(ref mutated) == 1;

        public int RowsAffected { get; private set; }
        public int SaveAttemptCount { get; private set; }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            SaveAttemptCount++;
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
            recoveryProbe?.MarkMutation();

            return result;
        }
    }

    private sealed class ApplicationRecoveryReadProbe
    {
        private int mutated;

        public bool Mutated => Volatile.Read(ref mutated) == 1;

        public void MarkMutation() => Volatile.Write(ref mutated, 1);
    }

    private sealed class ApplicationRecoveryReadObserver(
        ApplicationRecoveryReadProbe probe) : DbCommandInterceptor
    {
        public int RefundItemRecoveryReadCount { get; private set; }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (probe.Mutated &&
                command.CommandText.TrimStart().StartsWith(
                    "SELECT",
                    StringComparison.OrdinalIgnoreCase) &&
                command.CommandText.Contains(
                    "FROM \"REFUND_ITEM\"",
                    StringComparison.OrdinalIgnoreCase))
            {
                RefundItemRecoveryReadCount++;
            }

            return ValueTask.FromResult(result);
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

    private sealed class RecordingRefundLockCoordinator(AppDbContext db)
        : IRefundLockCoordinator
    {
        public List<string> Calls { get; } = [];
        public List<bool> TransactionObserved { get; } = [];

        public async Task<bool> LockRefundRequestAsync(
            long refundId,
            CancellationToken cancellationToken)
        {
            Calls.Add($"refund:{refundId}");
            TransactionObserved.Add(db.Database.CurrentTransaction is not null);
            return await db.Set<RefundRequest>()
                .AsNoTracking()
                .AnyAsync(item => item.RefundId == refundId, cancellationToken);
        }

        public async Task<bool> LockOrderAsync(
            long orderId,
            CancellationToken cancellationToken)
        {
            Calls.Add($"order:{orderId}");
            TransactionObserved.Add(db.Database.CurrentTransaction is not null);
            return await db.Set<Order>()
                .AsNoTracking()
                .AnyAsync(item => item.OrderId == orderId, cancellationToken);
        }
    }

    private sealed record RecordedOracleLockCommand(
        string CommandText,
        DbConnection Connection,
        DbTransaction Transaction,
        string ParameterName,
        DbType ParameterType,
        object? ParameterValue);

    private sealed class RecordingDbConnection : DbConnection
    {
        private ConnectionState state = ConnectionState.Closed;

        public List<RecordedOracleLockCommand> Commands { get; } = [];
        [AllowNull]
        public override string ConnectionString { get; set; } = "Data Source=:memory:";
        public override string Database => "refund-lock-test";
        public override string DataSource => "recording";
        public override string ServerVersion => "1.0";
        public override ConnectionState State => state;

        public override void ChangeDatabase(string databaseName)
        {
        }

        public override void Close() => state = ConnectionState.Closed;

        public override void Open() => state = ConnectionState.Open;

        public override Task OpenAsync(CancellationToken cancellationToken)
        {
            state = ConnectionState.Open;
            return Task.CompletedTask;
        }

        protected override DbTransaction BeginDbTransaction(
            IsolationLevel isolationLevel) => new RecordingDbTransaction(
            this,
            isolationLevel);

        protected override DbCommand CreateDbCommand() => new RecordingDbCommand(this);
    }

    private sealed class RecordingDbTransaction(
        RecordingDbConnection connection,
        IsolationLevel isolationLevel) : DbTransaction
    {
        public override IsolationLevel IsolationLevel => isolationLevel;
        protected override DbConnection DbConnection => connection;
        public override void Commit()
        {
        }

        public override void Rollback()
        {
        }
    }

    private sealed class RecordingDbCommand(RecordingDbConnection connection) : DbCommand
    {
        private readonly RecordingDbParameterCollection parameters = new();
        private DbTransaction? transaction;

        [AllowNull]
        public override string CommandText { get; set; } = string.Empty;
        public override int CommandTimeout { get; set; }
        public override CommandType CommandType { get; set; }
        public override bool DesignTimeVisible { get; set; }
        public override UpdateRowSource UpdatedRowSource { get; set; }
        protected override DbConnection? DbConnection { get; set; } = connection;
        protected override DbParameterCollection DbParameterCollection => parameters;
        protected override DbTransaction? DbTransaction
        {
            get => transaction;
            set => transaction = value;
        }

        public override void Cancel()
        {
        }

        public override int ExecuteNonQuery() => throw new NotSupportedException();

        public override object? ExecuteScalar() => Record();

        public override Task<object?> ExecuteScalarAsync(
            CancellationToken cancellationToken) => Task.FromResult<object?>(Record());

        public override void Prepare()
        {
        }

        protected override DbParameter CreateDbParameter() => new RecordingDbParameter();

        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) =>
            throw new NotSupportedException();

        private object Record()
        {
            var parameter = Assert.Single(parameters.Cast<DbParameter>());
            connection.Commands.Add(new RecordedOracleLockCommand(
                CommandText,
                DbConnection!,
                DbTransaction!,
                parameter.ParameterName,
                parameter.DbType,
                parameter.Value));
            return 1L;
        }
    }

    private sealed class RecordingDbParameter : DbParameter
    {
        public override DbType DbType { get; set; }
        public override ParameterDirection Direction { get; set; } = ParameterDirection.Input;
        public override bool IsNullable { get; set; }
        [AllowNull]
        public override string ParameterName { get; set; } = string.Empty;
        public override int Size { get; set; }
        [AllowNull]
        public override string SourceColumn { get; set; } = string.Empty;
        public override bool SourceColumnNullMapping { get; set; }
        public override object? Value { get; set; }

        public override void ResetDbType() => DbType = DbType.Object;
    }

    private sealed class RecordingDbParameterCollection : DbParameterCollection
    {
        private readonly List<DbParameter> items = [];

        public override int Count => items.Count;
        public override object SyncRoot => ((System.Collections.ICollection)items).SyncRoot;

        public override int Add(object value)
        {
            items.Add((DbParameter)value);
            return items.Count - 1;
        }

        public override void AddRange(Array values)
        {
            foreach (var value in values)
            {
                Add(value!);
            }
        }

        public override void Clear() => items.Clear();
        public override bool Contains(object value) => items.Contains((DbParameter)value);
        public override bool Contains(string value) => IndexOf(value) >= 0;
        public override void CopyTo(Array array, int index) =>
            ((System.Collections.ICollection)items).CopyTo(array, index);
        public override System.Collections.IEnumerator GetEnumerator() => items.GetEnumerator();
        public override int IndexOf(object value) => items.IndexOf((DbParameter)value);
        public override int IndexOf(string parameterName) => items.FindIndex(
            item => item.ParameterName == parameterName);
        public override void Insert(int index, object value) =>
            items.Insert(index, (DbParameter)value);
        public override void Remove(object value) => items.Remove((DbParameter)value);
        public override void RemoveAt(int index) => items.RemoveAt(index);
        public override void RemoveAt(string parameterName)
        {
            var index = IndexOf(parameterName);
            if (index >= 0)
            {
                RemoveAt(index);
            }
        }

        protected override DbParameter GetParameter(int index) => items[index];
        protected override DbParameter GetParameter(string parameterName) =>
            items[IndexOf(parameterName)];
        protected override void SetParameter(int index, DbParameter value) =>
            items[index] = value;
        protected override void SetParameter(string parameterName, DbParameter value)
        {
            var index = IndexOf(parameterName);
            if (index >= 0)
            {
                items[index] = value;
            }
            else
            {
                items.Add(value);
            }
        }
    }

    private sealed class ApproveEntityConcurrencyInterceptor(string token)
        : SaveChangesInterceptor
    {
        private int mutated;

        public int MutationAffectedRows { get; private set; }
        public int ConcurrencyExceptionObservedCount { get; private set; }
        public int SaveAttemptCount { get; private set; }
        public bool MutationObservedTransaction { get; private set; }
        public IReadOnlyList<Type> ConcurrentEntityTypes { get; private set; } = [];

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            SaveAttemptCount++;
            if (Interlocked.CompareExchange(ref mutated, 1, 0) != 0)
            {
                return result;
            }

            var db = (AppDbContext)eventData.Context!;
            MutationObservedTransaction = db.Database.CurrentTransaction is not null;
            MutationAffectedRows = token switch
            {
                "order-item" => await db.Database.ExecuteSqlRawAsync(
                    "UPDATE ORDER_ITEM SET ITEM_STATUS = 'EXCHANGING' " +
                    "WHERE ORDER_ITEM_ID = 101;",
                    cancellationToken),
                "ticket" => await db.Database.ExecuteSqlRawAsync(
                    "UPDATE E_TICKET SET TICKET_STATUS = 'USED' " +
                    "WHERE ORDER_ITEM_ID = 101;",
                    cancellationToken),
                "order" => await db.Database.ExecuteSqlRawAsync(
                    "UPDATE T_ORDER SET ORDER_STATUS = 'CANCELLED' " +
                    "WHERE ORDER_ID = 11;",
                    cancellationToken),
                _ => throw new ArgumentOutOfRangeException(nameof(token), token, null),
            };
            return result;
        }

        public override ValueTask<InterceptionResult> ThrowingConcurrencyExceptionAsync(
            ConcurrencyExceptionEventData eventData,
            InterceptionResult result,
            CancellationToken cancellationToken = default)
        {
            ConcurrencyExceptionObservedCount++;
            ConcurrentEntityTypes = eventData.Exception.Entries
                .Select(entry => entry.Metadata.ClrType)
                .ToList();
            return ValueTask.FromResult(result);
        }
    }

    private sealed class ReviewedAfterRollbackInterceptor : DbTransactionInterceptor
    {
        private int mutated;

        public int MutationAffectedRows { get; private set; }

        public override async Task TransactionRolledBackAsync(
            DbTransaction transaction,
            TransactionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.CompareExchange(ref mutated, 1, 0) != 0)
            {
                return;
            }

            var connection = eventData.Context?.Database.GetDbConnection() ??
                throw new InvalidOperationException("Expected the rolled-back context.");
            await using var command = connection.CreateCommand();
            command.CommandText =
                "UPDATE REFUND_REQUEST " +
                "SET APPROVE_STATUS = 'APPROVED', REFUND_STATUS = 'COMPLETED' " +
                "WHERE REFUND_ID = 401;";
            MutationAffectedRows = await command.ExecuteNonQueryAsync(cancellationToken);
        }
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

    private sealed class RefundBulkUpdateObserver : DbCommandInterceptor
    {
        public int PaymentUpdateAttempts { get; private set; }
        public int ReservationUpdateAttempts { get; private set; }
        public int PaymentUpdateRows { get; private set; }
        public int ReservationUpdateRows { get; private set; }
        public int RefundRequestRecoveryReadCount { get; private set; }
        public int TrackedPaymentsAtUpdate { get; private set; } = -1;
        public int TrackedReservationsAtUpdate { get; private set; } = -1;

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            var db = eventData.Context!;
            if (IsUpdate(command.CommandText, "PAYMENT"))
            {
                PaymentUpdateAttempts++;
                TrackedPaymentsAtUpdate = db.ChangeTracker.Entries<Payment>().Count();
            }

            if (IsUpdate(command.CommandText, "SEAT_RESERVATION"))
            {
                ReservationUpdateAttempts++;
                TrackedReservationsAtUpdate = db.ChangeTracker
                    .Entries<SeatReservation>()
                    .Count();
            }

            return ValueTask.FromResult(result);
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (PaymentUpdateAttempts > 0 &&
                command.CommandText.TrimStart().StartsWith(
                    "SELECT",
                    StringComparison.OrdinalIgnoreCase) &&
                command.CommandText.Contains(
                    "FROM \"REFUND_REQUEST\"",
                    StringComparison.OrdinalIgnoreCase))
            {
                RefundRequestRecoveryReadCount++;
            }

            return ValueTask.FromResult(result);
        }

        public override ValueTask<int> NonQueryExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            int result,
            CancellationToken cancellationToken = default)
        {
            if (IsUpdate(command.CommandText, "PAYMENT"))
            {
                PaymentUpdateRows += result;
            }

            if (IsUpdate(command.CommandText, "SEAT_RESERVATION"))
            {
                ReservationUpdateRows += result;
            }

            return ValueTask.FromResult(result);
        }

        private static bool IsUpdate(string commandText, string tableName) =>
            commandText.TrimStart().StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase) &&
            commandText.Contains(tableName, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class StableRefundItemOrderObserver : DbCommandInterceptor
    {
        public IReadOnlyList<long> ReservationOrderItemIds { get; private set; } = [];
        public IReadOnlyList<string> ParameterValues { get; private set; } = [];
        public string? ReservationCommandText { get; private set; }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (!command.CommandText.TrimStart().StartsWith(
                    "UPDATE",
                    StringComparison.OrdinalIgnoreCase) ||
                !command.CommandText.Contains(
                    "SEAT_RESERVATION",
                    StringComparison.OrdinalIgnoreCase))
            {
                return ValueTask.FromResult(result);
            }

            ReservationCommandText = command.CommandText;
            ParameterValues = command.Parameters
                .Cast<DbParameter>()
                .Select(parameter => $"{parameter.ParameterName}={parameter.Value}")
                .ToList();
            var scalarIds = command.Parameters
                .Cast<DbParameter>()
                .Where(parameter => parameter.ParameterName.Contains(
                    "orderItemIds",
                    StringComparison.OrdinalIgnoreCase))
                .Select(parameter => Convert.ToInt64(parameter.Value, CultureInfo.InvariantCulture))
                .ToArray();
            if (scalarIds.Length > 0)
            {
                ReservationOrderItemIds = scalarIds;
                return ValueTask.FromResult(result);
            }

            foreach (DbParameter parameter in command.Parameters)
            {
                if (parameter.Value is not string json ||
                    !json.TrimStart().StartsWith("[", StringComparison.Ordinal))
                {
                    continue;
                }

                var values = JsonSerializer.Deserialize<long[]>(json);
                if (values is { Length: > 0 })
                {
                    ReservationOrderItemIds = values;
                    break;
                }
            }

            return ValueTask.FromResult(result);
        }
    }

    private sealed class ApproveRequestConcurrencyInterceptor : SaveChangesInterceptor
    {
        private int mutated;

        public bool Mutated => Volatile.Read(ref mutated) == 1;
        public decimal ObservedRefundAmountBeforeMutation { get; private set; }
        public string? ObservedReservationStatusBeforeMutation { get; private set; }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.CompareExchange(ref mutated, 1, 0) != 0)
            {
                return result;
            }

            var db = (AppDbContext)eventData.Context!;
            ObservedRefundAmountBeforeMutation = await db.Set<Payment>()
                .AsNoTracking()
                .Where(item => item.PaymentId == 31)
                .Select(item => item.RefundAmount)
                .SingleAsync(cancellationToken);
            ObservedReservationStatusBeforeMutation = await db.Set<SeatReservation>()
                .AsNoTracking()
                .Where(item => item.OrderItemId == 101)
                .Select(item => item.ReservationStatus)
                .SingleAsync(cancellationToken);
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE REFUND_REQUEST SET APPROVE_STATUS = 'APPROVED', " +
                "REFUND_STATUS = 'COMPLETED' WHERE REFUND_ID = 401;",
                cancellationToken);
            return result;
        }
    }
}
