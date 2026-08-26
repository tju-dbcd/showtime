using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using ShowtimeBackend.Entities.UserPermission;

namespace ShowtimeBackend.Tests.UserPermission;

public sealed class OperationLogIntegrationTests
{
    [Fact]
    public async Task LoginSuccessAndFailure_ArePersistedWithoutCredentials()
    {
        using var factory = new AuthTestFactory();
        await factory.ResetDatabaseAsync();
        await factory.SeedRoleAsync();
        using var client = factory.CreateApiClient();
        var registration = await client.PostAsJsonAsync(
            "/api/auth/register",
            TestRequests.ValidRegistration());
        registration.EnsureSuccessStatusCode();

        var success = await client.PostAsJsonAsync(
            "/api/auth/login",
            TestRequests.Login("alice"));
        var failure = await client.PostAsJsonAsync(
            "/api/auth/login",
            TestRequests.Login("alice") with { Password = "wrong-password" });

        success.EnsureSuccessStatusCode();
        Assert.False(failure.IsSuccessStatusCode);
        var logs = await factory.ExecuteDbContextAsync(async db =>
            await db.Set<OperationLog>()
                .AsNoTracking()
                .Where(item => item.OperationModule == "AUTH" && item.OperationType == "LOGIN")
                .OrderBy(item => item.LogId)
                .ToListAsync());

        Assert.Equal(2, logs.Count);
        Assert.True(logs[0].Status);
        Assert.False(logs[1].Status);
        var serialized = string.Join(
            " ",
            logs.Select(item => $"{item.RequestParams} {item.ResponseResult} {item.ErrorMsg}"));
        Assert.DoesNotContain("Password123", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("wrong-password", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("eyJ", serialized, StringComparison.Ordinal);
    }
}
