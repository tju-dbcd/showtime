using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using ShowtimeBackend.DTOs.OrderTicket;

namespace ShowtimeBackend.Tests.OrderTicket;

public sealed class RefundControllersTests
{
    [Fact]
    public async Task Create_WithoutAdminRole_ReturnsForbidden()
    {
        using var factory = new AuthTestFactory();
        using var client = factory.CreateApiClient();
        var token = RefundTestData.CreateToken(7, "alice", "USER");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync(
            "/api/admin/refund-policies",
            new SaveRefundPolicyRequest(null, "全局", 24, 0.8m, 0m, 1, null));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Create_AsAdmin_ReturnsCreatedAndUsesTokenUserAsActor()
    {
        using var factory = new AuthTestFactory();
        await factory.ResetDatabaseAsync();
        using var client = factory.CreateApiClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            RefundTestData.CreateToken(7, "policy-admin", "Admin"));

        var response = await client.PostAsJsonAsync(
            "/api/admin/refund-policies",
            new SaveRefundPolicyRequest(null, "  全局策略  ", 24, 0.8m, 0m, 1, null));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var policy = await factory.ExecuteDbContextAsync(async db =>
            await db.Set<ShowtimeBackend.Entities.OrderTicket.RefundPolicy>().SingleAsync());
        Assert.Equal("全局策略", policy.PolicyName);
        Assert.Equal("policy-admin", policy.CreateBy);
    }

    [Fact]
    public async Task UpdateStatus_WithInvalidStatus_ReturnsBadRequestEnvelope()
    {
        using var factory = new AuthTestFactory();
        await factory.ResetDatabaseAsync();
        var policyId = await factory.ExecuteDbContextAsync(async db =>
        {
            var policy = new ShowtimeBackend.Entities.OrderTicket.RefundPolicy
            {
                PolicyName = "全局策略",
                RefundDeadlineHour = 24,
                RefundRate = 0.8m,
                ServiceFee = 0m,
                Priority = 1,
                Status = 1,
                CreateBy = "seed",
                UpdateBy = "seed",
            };
            db.Add(policy);
            await db.SaveChangesAsync();
            return policy.PolicyId;
        });
        using var client = factory.CreateApiClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            RefundTestData.CreateToken(8, "admin", "Admin"));

        var response = await client.PatchAsJsonAsync(
            $"/api/admin/refund-policies/{policyId}/status",
            new UpdateRefundPolicyStatusRequest(2));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ShowtimeBackend.Common.ApiResponse<object>>();
        Assert.False(body!.Success);
        Assert.Equal("REFUND_POLICY_INVALID_STATUS", body.Code);
    }
}
