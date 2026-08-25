using Microsoft.EntityFrameworkCore;
using ShowtimeBackend.DTOs.OrderTicket;
using ShowtimeBackend.Entities.OrderTicket;
using ShowtimeBackend.Entities.ShowSession;
using ShowtimeBackend.Services.OrderTicket;

namespace ShowtimeBackend.Tests.OrderTicket;

public sealed class RefundPolicyAdminServiceTests
{
    [Fact]
    public async Task CreateAsync_PersistsValidatedGlobalPolicy()
    {
        await using var fixture = await RefundTestData.CreateAsync();
        var service = new RefundPolicyAdminService(fixture.Db);
        var request = new SaveRefundPolicyRequest(null, "全局72小时", 72, 0.8m, 5m, 1, null);

        var result = await service.CreateAsync("admin", request, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0.8m, result.Value!.RefundRate);
        Assert.Equal("admin", (await fixture.Db.Set<RefundPolicy>().SingleAsync()).CreateBy);
    }

    [Fact]
    public async Task CreateAsync_ReturnsNotFoundWhenShowDoesNotExist()
    {
        await using var fixture = await RefundTestData.CreateAsync();
        var service = new RefundPolicyAdminService(fixture.Db);

        var result = await service.CreateAsync(
            "admin",
            new SaveRefundPolicyRequest(404, "专属", 24, 0.8m, 0m, 1, null),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(OrderTicketFailure.NotFound, result.Failure);
        Assert.Equal("REFUND_POLICY_SHOW_NOT_FOUND", result.ErrorCode);
    }

    [Theory]
    [MemberData(nameof(InvalidRequests))]
    public async Task CreateAsync_ReturnsBadRequestForInvalidRequest(SaveRefundPolicyRequest request)
    {
        await using var fixture = await RefundTestData.CreateAsync();
        var service = new RefundPolicyAdminService(fixture.Db);

        var result = await service.CreateAsync("admin", request, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(OrderTicketFailure.InvalidRequest, result.Failure);
        Assert.Equal("REFUND_POLICY_INVALID", result.ErrorCode);
        Assert.Empty(await fixture.Db.Set<RefundPolicy>().ToListAsync());
    }

    [Fact]
    public async Task CreateAsync_PersistsOracleNumberBoundaryValuesIncludingTrailingZeroes()
    {
        await using var fixture = await RefundTestData.CreateAsync();
        var service = new RefundPolicyAdminService(fixture.Db);

        var result = await service.CreateAsync(
            "admin",
            new SaveRefundPolicyRequest(
                null,
                "精度边界",
                99999,
                0.123400m,
                99999999.9900m,
                99999,
                null),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(99999, result.Value!.RefundDeadlineHour);
        Assert.Equal(0.1234m, result.Value.RefundRate);
        Assert.Equal(99999999.99m, result.Value.ServiceFee);
        Assert.Equal(99999, result.Value.Priority);
    }

    [Fact]
    public async Task UpdateAsync_ChangesOnlyTargetPolicyAndUsesActor()
    {
        await using var fixture = await RefundTestData.CreateAsync();
        fixture.Db.AddRange(
            Policy(1, "原策略"),
            Policy(2, "保持不变"));
        await fixture.Db.SaveChangesAsync();
        var service = new RefundPolicyAdminService(fixture.Db);

        var result = await service.UpdateAsync(
            "editor",
            1,
            new SaveRefundPolicyRequest(null, "更新策略", 48, 0.5m, 3m, 2, "新申请生效"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var policies = await fixture.Db.Set<RefundPolicy>().OrderBy(item => item.PolicyId).ToListAsync();
        Assert.Equal("更新策略", policies[0].PolicyName);
        Assert.Equal("editor", policies[0].UpdateBy);
        Assert.Equal("保持不变", policies[1].PolicyName);
    }

    [Fact]
    public async Task UpdateAsync_PersistsOracleNumberBoundaryValuesIncludingTrailingZeroes()
    {
        await using var fixture = await RefundTestData.CreateAsync();
        fixture.Db.Add(Policy(1, "原策略"));
        await fixture.Db.SaveChangesAsync();
        var service = new RefundPolicyAdminService(fixture.Db);

        var result = await service.UpdateAsync(
            "editor",
            1,
            new SaveRefundPolicyRequest(
                null,
                "精度边界",
                99999,
                0.123400m,
                99999999.9900m,
                99999,
                null),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        fixture.Db.ChangeTracker.Clear();
        var persisted = await fixture.Db.Set<RefundPolicy>().SingleAsync();
        Assert.Equal(99999, persisted.RefundDeadlineHour);
        Assert.Equal(0.1234m, persisted.RefundRate);
        Assert.Equal(99999999.99m, persisted.ServiceFee);
        Assert.Equal(99999, persisted.Priority);
    }

    [Theory]
    [MemberData(nameof(InvalidOracleNumberRequests))]
    public async Task UpdateAsync_RejectsInvalidOracleNumberValuesWithoutChangingEntity(
        SaveRefundPolicyRequest request)
    {
        await using var fixture = await RefundTestData.CreateAsync();
        fixture.Db.Add(Policy(1, "原策略"));
        await fixture.Db.SaveChangesAsync();
        var service = new RefundPolicyAdminService(fixture.Db);

        var result = await service.UpdateAsync("editor", 1, request, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(OrderTicketFailure.InvalidRequest, result.Failure);
        Assert.Equal("REFUND_POLICY_INVALID", result.ErrorCode);
        fixture.Db.ChangeTracker.Clear();
        var persisted = await fixture.Db.Set<RefundPolicy>().SingleAsync();
        Assert.Equal("原策略", persisted.PolicyName);
        Assert.Equal(24, persisted.RefundDeadlineHour);
        Assert.Equal(0.8m, persisted.RefundRate);
        Assert.Equal(0m, persisted.ServiceFee);
        Assert.Equal(1, persisted.Priority);
    }

    [Fact]
    public async Task UpdateStatusAsync_RejectsStatusOutsideZeroAndOne()
    {
        await using var fixture = await RefundTestData.CreateAsync();
        fixture.Db.Add(Policy(1, "策略"));
        await fixture.Db.SaveChangesAsync();
        var service = new RefundPolicyAdminService(fixture.Db);

        var result = await service.UpdateStatusAsync(
            "admin",
            1,
            new UpdateRefundPolicyStatusRequest(2),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(OrderTicketFailure.InvalidRequest, result.Failure);
        Assert.Equal((byte)1, (await fixture.Db.Set<RefundPolicy>().SingleAsync()).Status);
    }

    [Fact]
    public async Task ListAsync_UsesStablePriorityThenPolicyIdSortAndLimitsPageSize()
    {
        await using var fixture = await RefundTestData.CreateAsync();
        fixture.Db.AddRange(
            Policy(3, "第三", priority: 2),
            Policy(2, "第二", priority: 1),
            Policy(1, "第一", priority: 1));
        await fixture.Db.SaveChangesAsync();
        var service = new RefundPolicyAdminService(fixture.Db);

        var list = await service.ListAsync(new RefundPolicyListQuery(null, null, 1, 2), CancellationToken.None);
        var invalid = await service.ListAsync(new RefundPolicyListQuery(null, null, 1, 101), CancellationToken.None);

        Assert.True(list.IsSuccess);
        Assert.Equal([1L, 2L], list.Value!.Items.Select(item => item.PolicyId));
        Assert.Equal(3, list.Value.TotalCount);
        Assert.False(invalid.IsSuccess);
        Assert.Equal(OrderTicketFailure.InvalidRequest, invalid.Failure);
    }

    [Fact]
    public async Task ListAsync_PutsGlobalPoliciesFirstBeforeApplyingDeadlinePriorityAndIdSort()
    {
        await using var fixture = await RefundTestData.CreateAsync();
        fixture.Db.AddRange(
            Policy(1, "专属截止早", showId: 101, deadlineHour: 12),
            Policy(2, "全局截止晚", deadlineHour: 72),
            Policy(3, "专属截止晚", showId: 102, deadlineHour: 96),
            Policy(4, "全局截止早", deadlineHour: 48));
        await fixture.Db.SaveChangesAsync();
        var service = new RefundPolicyAdminService(fixture.Db);

        var result = await service.ListAsync(
            new RefundPolicyListQuery(null, null, 1, 20),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal([2L, 4L, 3L, 1L], result.Value!.Items.Select(item => item.PolicyId));
    }

    public static IEnumerable<object[]> InvalidRequests()
    {
        yield return [new SaveRefundPolicyRequest(null, " ", 24, 0.8m, 0m, 1, null)];
        yield return [new SaveRefundPolicyRequest(null, new string('a', 101), 24, 0.8m, 0m, 1, null)];
        yield return [new SaveRefundPolicyRequest(null, "策略", -1, 0.8m, 0m, 1, null)];
        yield return [new SaveRefundPolicyRequest(null, "策略", 24, 1.1m, 0m, 1, null)];
        yield return [new SaveRefundPolicyRequest(null, "策略", 24, -0.1m, 0m, 1, null)];
        yield return [new SaveRefundPolicyRequest(null, "策略", 24, 0.8m, -0.01m, 1, null)];
        yield return [new SaveRefundPolicyRequest(null, "策略", 24, 0.8m, 0m, 0, null)];
        yield return [new SaveRefundPolicyRequest(null, "策略", 24, 0.8m, 0m, 1, new string('a', 501))];
        yield return [new SaveRefundPolicyRequest(null, "策略", 100000, 0.8m, 0m, 1, null)];
        yield return [new SaveRefundPolicyRequest(null, "策略", 24, 0.8m, 0m, 100000, null)];
        yield return [new SaveRefundPolicyRequest(null, "策略", 24, 0.12345m, 0m, 1, null)];
        yield return [new SaveRefundPolicyRequest(null, "策略", 24, 0.8m, 0.001m, 1, null)];
        yield return [new SaveRefundPolicyRequest(null, "策略", 24, 0.8m, 100000000m, 1, null)];
    }

    public static IEnumerable<object[]> InvalidOracleNumberRequests()
    {
        yield return [new SaveRefundPolicyRequest(null, "更新", 100000, 0.8m, 0m, 1, null)];
        yield return [new SaveRefundPolicyRequest(null, "更新", 24, 0.8m, 0m, 100000, null)];
        yield return [new SaveRefundPolicyRequest(null, "更新", 24, 0.12345m, 0m, 1, null)];
        yield return [new SaveRefundPolicyRequest(null, "更新", 24, 0.8m, 0.001m, 1, null)];
        yield return [new SaveRefundPolicyRequest(null, "更新", 24, 0.8m, 100000000m, 1, null)];
    }

    private static RefundPolicy Policy(
        long policyId,
        string name,
        int priority = 1,
        long? showId = null,
        int deadlineHour = 24) => new()
        {
            PolicyId = policyId,
            ShowId = showId,
            PolicyName = name,
            RefundDeadlineHour = deadlineHour,
            RefundRate = 0.8m,
            ServiceFee = 0m,
            Priority = priority,
            Status = 1,
            CreateTime = new DateTime(2026, 8, 24),
            UpdateTime = new DateTime(2026, 8, 24),
            CreateBy = "seed",
            UpdateBy = "seed",
        };
}
