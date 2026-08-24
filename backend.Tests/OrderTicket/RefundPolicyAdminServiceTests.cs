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
    }

    private static RefundPolicy Policy(long policyId, string name, int priority = 1) => new()
    {
        PolicyId = policyId,
        PolicyName = name,
        RefundDeadlineHour = 24,
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
