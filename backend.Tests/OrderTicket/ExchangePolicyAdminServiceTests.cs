using Microsoft.EntityFrameworkCore;
using ShowtimeBackend.DTOs.OrderTicket;
using ShowtimeBackend.Entities.OrderTicket;
using ShowtimeBackend.Services.OrderTicket;

namespace ShowtimeBackend.Tests.OrderTicket;

public sealed class ExchangePolicyAdminServiceTests
{
    [Fact]
    public async Task CreateAsync_PersistsValidatedGlobalPolicyAndActor()
    {
        await using var fixture = await RefundTestData.CreateAsync();
        var service = new ExchangePolicyAdminService(fixture.Db);

        var result = await service.CreateAsync(
            "admin",
            new SaveExchangePolicyRequest(null, "全局改签", 72, 5m, 1, 3, null),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal((byte)1, result.Value!.AllowCrossSession);
        var persisted = await fixture.Db.Set<ExchangePolicy>().SingleAsync();
        Assert.Equal("admin", persisted.CreateBy);
        Assert.Equal((byte)1, persisted.Status);
    }

    [Fact]
    public async Task CreateAsync_ReturnsNotFoundWhenShowDoesNotExist()
    {
        await using var fixture = await RefundTestData.CreateAsync();
        var service = new ExchangePolicyAdminService(fixture.Db);

        var result = await service.CreateAsync(
            "admin",
            new SaveExchangePolicyRequest(404, "专属改签", 24, 0m, 0, 1, null),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(OrderTicketFailure.NotFound, result.Failure);
        Assert.Equal("EXCHANGE_POLICY_SHOW_NOT_FOUND", result.ErrorCode);
    }

    [Theory]
    [MemberData(nameof(InvalidRequests))]
    public async Task CreateAsync_RejectsInvalidOracleValues(SaveExchangePolicyRequest request)
    {
        await using var fixture = await RefundTestData.CreateAsync();
        var service = new ExchangePolicyAdminService(fixture.Db);

        var result = await service.CreateAsync("admin", request, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(OrderTicketFailure.InvalidRequest, result.Failure);
        Assert.Equal("EXCHANGE_POLICY_INVALID", result.ErrorCode);
        Assert.Empty(await fixture.Db.Set<ExchangePolicy>().ToListAsync());
    }

    [Fact]
    public async Task CreateAsync_AcceptsOracleNumberBoundaries()
    {
        await using var fixture = await RefundTestData.CreateAsync();
        var service = new ExchangePolicyAdminService(fixture.Db);

        var result = await service.CreateAsync(
            "admin",
            new SaveExchangePolicyRequest(
                null,
                "边界策略",
                99999,
                99999999.990m,
                0,
                99999,
                null),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(99999999.99m, result.Value!.ExchangeFee);
        Assert.Equal(99999, result.Value.ExchangeDeadlineHour);
        Assert.Equal(99999, result.Value.Priority);
    }

    [Fact]
    public async Task UpdateAsync_ChangesTargetAndPreservesStatus()
    {
        await using var fixture = await RefundTestData.CreateAsync();
        fixture.Db.AddRange(Policy(1, "原策略", status: 0), Policy(2, "保持不变"));
        await fixture.Db.SaveChangesAsync();
        var service = new ExchangePolicyAdminService(fixture.Db);

        var result = await service.UpdateAsync(
            "editor",
            1,
            new SaveExchangePolicyRequest(null, "更新策略", 48, 3m, 0, 2, "new"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal((byte)0, result.Value!.Status);
        Assert.Equal((byte)0, result.Value.AllowCrossSession);
        Assert.Equal("editor", (await fixture.Db.Set<ExchangePolicy>().FindAsync(1L))!.UpdateBy);
        Assert.Equal("保持不变", (await fixture.Db.Set<ExchangePolicy>().FindAsync(2L))!.PolicyName);
    }

    [Fact]
    public async Task UpdateStatusAsync_OnlyAcceptsZeroAndOne()
    {
        await using var fixture = await RefundTestData.CreateAsync();
        fixture.Db.Add(Policy(1, "策略"));
        await fixture.Db.SaveChangesAsync();
        var service = new ExchangePolicyAdminService(fixture.Db);

        var invalid = await service.UpdateStatusAsync(
            "admin", 1, new UpdateExchangePolicyStatusRequest(2), CancellationToken.None);
        var valid = await service.UpdateStatusAsync(
            "admin", 1, new UpdateExchangePolicyStatusRequest(0), CancellationToken.None);

        Assert.False(invalid.IsSuccess);
        Assert.Equal("EXCHANGE_POLICY_INVALID_STATUS", invalid.ErrorCode);
        Assert.True(valid.IsSuccess);
        Assert.Equal((byte)0, valid.Value!.Status);
    }

    [Fact]
    public async Task ListAsync_UsesGlobalThenPriorityDescendingThenPolicyIdOrder()
    {
        await using var fixture = await RefundTestData.CreateAsync();
        fixture.Db.AddRange(
            Policy(4, "专属高", priority: 9, showId: 101),
            Policy(3, "全局低", priority: 1),
            Policy(2, "全局高后", priority: 5),
            Policy(1, "全局高前", priority: 5));
        await fixture.Db.SaveChangesAsync();
        var service = new ExchangePolicyAdminService(fixture.Db);

        var result = await service.ListAsync(
            new ExchangePolicyListQuery(null, null, 1, 20), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal([1L, 2L, 3L, 4L], result.Value!.Items.Select(item => item.PolicyId));
    }

    [Theory]
    [InlineData(0, 20)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    public async Task ListAsync_RejectsInvalidPaging(int page, int pageSize)
    {
        await using var fixture = await RefundTestData.CreateAsync();
        var service = new ExchangePolicyAdminService(fixture.Db);

        var result = await service.ListAsync(
            new ExchangePolicyListQuery(null, null, page, pageSize), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(OrderTicketFailure.InvalidRequest, result.Failure);
    }

    public static IEnumerable<object[]> InvalidRequests()
    {
        yield return [new SaveExchangePolicyRequest(null, " ", 24, 0m, 1, 1, null)];
        yield return [new SaveExchangePolicyRequest(null, new string('a', 101), 24, 0m, 1, 1, null)];
        yield return [new SaveExchangePolicyRequest(null, "策略", -1, 0m, 1, 1, null)];
        yield return [new SaveExchangePolicyRequest(null, "策略", 100000, 0m, 1, 1, null)];
        yield return [new SaveExchangePolicyRequest(null, "策略", 24, -0.01m, 1, 1, null)];
        yield return [new SaveExchangePolicyRequest(null, "策略", 24, 0.001m, 1, 1, null)];
        yield return [new SaveExchangePolicyRequest(null, "策略", 24, 100000000m, 1, 1, null)];
        yield return [new SaveExchangePolicyRequest(null, "策略", 24, 0m, 2, 1, null)];
        yield return [new SaveExchangePolicyRequest(null, "策略", 24, 0m, 1, 0, null)];
        yield return [new SaveExchangePolicyRequest(null, "策略", 24, 0m, 1, 100000, null)];
        yield return [new SaveExchangePolicyRequest(null, "策略", 24, 0m, 1, 1, new string('a', 501))];
        yield return [new SaveExchangePolicyRequest(0, "策略", 24, 0m, 1, 1, null)];
    }

    private static ExchangePolicy Policy(
        long id,
        string name,
        int priority = 1,
        long? showId = null,
        byte status = 1) => new()
        {
            PolicyId = id,
            ShowId = showId,
            PolicyName = name,
            ExchangeDeadlineHour = 24,
            ExchangeFee = 0m,
            AllowCrossSession = 1,
            Priority = priority,
            Status = status,
            CreateTime = new DateTime(2026, 8, 30),
            UpdateTime = new DateTime(2026, 8, 30),
            CreateBy = "seed",
            UpdateBy = "seed",
        };
}
