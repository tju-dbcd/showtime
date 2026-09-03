using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using ShowtimeBackend.DTOs.UserPermission;
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

    [Fact]
    public async Task RefreshAndReplay_AreAuditedWithoutTokenMaterial()
    {
        using var factory = new AuthTestFactory();
        await factory.ResetDatabaseAsync();
        await factory.SeedRoleAsync();
        using var client = factory.CreateApiClient();
        (await client.PostAsJsonAsync(
            "/api/auth/register",
            TestRequests.ValidRegistration())).EnsureSuccessStatusCode();
        var loginResponse = await client.PostAsJsonAsync(
            "/api/auth/login",
            TestRequests.Login("alice"));
        loginResponse.EnsureSuccessStatusCode();
        var login = (await AuthTestFactory
            .ReadResponseAsync<LoginResponse>(loginResponse)).Data!;

        var refreshResponse = await client.PostAsJsonAsync(
            "/api/auth/refresh",
            new RefreshTokenRequest { RefreshToken = login.RefreshToken });
        refreshResponse.EnsureSuccessStatusCode();
        var refreshed = (await AuthTestFactory
            .ReadResponseAsync<RefreshTokenResponse>(refreshResponse)).Data!;
        var replay = await client.PostAsJsonAsync(
            "/api/auth/refresh",
            new RefreshTokenRequest { RefreshToken = login.RefreshToken });
        Assert.False(replay.IsSuccessStatusCode);

        var logs = await factory.ExecuteDbContextAsync(db =>
            db.Set<OperationLog>()
                .AsNoTracking()
                .Where(item => item.OperationModule == "AUTH")
                .OrderBy(item => item.LogId)
                .ToListAsync());
        Assert.Contains(logs, log => log.OperationType == "REFRESH_TOKEN"
            && log.Status);
        Assert.Contains(logs, log => log.OperationType == "REFRESH_TOKEN"
            && !log.Status);
        Assert.Contains(logs, log => log.OperationType == "REFRESH_TOKEN_REUSE"
            && !log.Status);

        var serialized = string.Join(
            " ",
            logs.Select(log =>
                $"{log.RequestParams} {log.ResponseResult} {log.ErrorMsg}"));
        Assert.DoesNotContain(login.RefreshToken, serialized, StringComparison.Ordinal);
        Assert.DoesNotContain(refreshed.RefreshToken, serialized, StringComparison.Ordinal);
        Assert.DoesNotContain(login.AccessToken, serialized, StringComparison.Ordinal);
        Assert.DoesNotContain(refreshed.AccessToken, serialized, StringComparison.Ordinal);
    }
}
