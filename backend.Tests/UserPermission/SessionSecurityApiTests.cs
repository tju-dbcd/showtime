using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using ShowtimeBackend.Common;
using ShowtimeBackend.DTOs.UserPermission;
using ShowtimeBackend.Entities.UserPermission;
using ShowtimeBackend.Services.UserPermission;

namespace ShowtimeBackend.Tests.UserPermission;

public sealed class SessionSecurityApiTests
{
    [Fact]
    public async Task DifferentUserAgentLogin_LocksOldSessionAndInvalidatesOldJwt()
    {
        using var factory = await CreateReadyFactoryAsync();
        using var firstClient = factory.CreateApiClient();
        firstClient.DefaultRequestHeaders.UserAgent.ParseAdd("Device-A/1.0");
        await RegisterAsync(firstClient, TestRequests.ValidRegistration());
        var firstLogin = await LoginAsync(firstClient, "alice");

        using var secondClient = factory.CreateApiClient();
        secondClient.DefaultRequestHeaders.UserAgent.ParseAdd("Device-B/1.0");
        var secondLogin = await LoginAsync(secondClient, "alice");

        Authorize(firstClient, firstLogin.AccessToken);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await firstClient.GetAsync("/api/test-authorization/user")).StatusCode);
        Authorize(secondClient, secondLogin.AccessToken);
        Assert.Equal(
            HttpStatusCode.OK,
            (await secondClient.GetAsync("/api/test-authorization/user")).StatusCode);

        var sessions = await factory.ExecuteDbContextAsync(dbContext =>
            dbContext.Set<UserSession>()
                .AsNoTracking()
                .OrderBy(session => session.LoginTime)
                .ThenBy(session => session.UserSessionId)
                .ToListAsync());
        Assert.Equal(2, sessions.Count);
        Assert.Equal(UserSessionStatuses.Locked, sessions[0].Status);
        Assert.True(sessions[0].RiskFlag);
        Assert.Equal(UserSessionStatuses.Active, sessions[1].Status);
        Assert.False(sessions[1].RiskFlag);
    }

    [Fact]
    public async Task SameClientLogin_LogsOutOldSessionWithoutRiskFlag()
    {
        using var factory = await CreateReadyFactoryAsync();
        using var client = factory.CreateApiClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Same-Device/1.0");
        await RegisterAsync(client, TestRequests.ValidRegistration());

        await LoginAsync(client, "alice");
        var current = await LoginAsync(client, "alice");
        Authorize(client, current.AccessToken);
        var response = await client.GetAsync("/api/auth/sessions");
        response.EnsureSuccessStatusCode();
        var envelope = await AuthTestFactory
            .ReadResponseAsync<IReadOnlyList<UserSessionResponse>>(response);

        Assert.Equal(2, envelope.Data!.Count);
        Assert.Single(envelope.Data, session => session.IsCurrent);
        var old = envelope.Data.Single(session => !session.IsCurrent);
        Assert.Equal(UserSessionStatuses.Logout, old.Status);
        Assert.False(old.RiskFlag);
    }

    [Fact]
    public async Task Refresh_RotatesToken_AndOldTokenReuseLocksSession()
    {
        using var factory = await CreateReadyFactoryAsync();
        using var client = factory.CreateApiClient();
        await RegisterAsync(client, TestRequests.ValidRegistration());
        var login = await LoginAsync(client, "alice");

        var refreshResponse = await client.PostAsJsonAsync(
            "/api/auth/refresh",
            new RefreshTokenRequest { RefreshToken = login.RefreshToken });
        refreshResponse.EnsureSuccessStatusCode();
        var refreshed = (await AuthTestFactory
            .ReadResponseAsync<RefreshTokenResponse>(refreshResponse)).Data!;
        Assert.NotEqual(login.RefreshToken, refreshed.RefreshToken);
        Authorize(client, refreshed.AccessToken);
        Assert.Equal(
            HttpStatusCode.OK,
            (await client.GetAsync("/api/test-authorization/user")).StatusCode);

        client.DefaultRequestHeaders.Authorization = null;
        var replay = await client.PostAsJsonAsync(
            "/api/auth/refresh",
            new RefreshTokenRequest { RefreshToken = login.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
        var replayBody = await AuthTestFactory
            .ReadResponseAsync<RefreshTokenResponse>(replay);
        Assert.Equal("AUTH_REFRESH_TOKEN_REUSED", replayBody.Code);

        Authorize(client, refreshed.AccessToken);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await client.GetAsync("/api/test-authorization/user")).StatusCode);
        client.DefaultRequestHeaders.Authorization = null;
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync(
                "/api/auth/refresh",
                new RefreshTokenRequest
                {
                    RefreshToken = refreshed.RefreshToken,
                })).StatusCode);
    }

    [Fact]
    public async Task ForgedAndExpiredRefreshTokens_AreRejectedSafely()
    {
        using var factory = await CreateReadyFactoryAsync();
        using var client = factory.CreateApiClient();
        await RegisterAsync(client, TestRequests.ValidRegistration());
        var login = await LoginAsync(client, "alice");

        var parts = login.RefreshToken.Split('.');
        parts[2] = parts[2][..^1] + (parts[2][^1] == 'A' ? "B" : "A");
        var forged = await client.PostAsJsonAsync(
            "/api/auth/refresh",
            new RefreshTokenRequest { RefreshToken = string.Join('.', parts) });
        Assert.Equal(HttpStatusCode.Unauthorized, forged.StatusCode);
        Assert.True(await factory.ExecuteDbContextAsync(dbContext =>
            dbContext.Set<UserSession>().AnyAsync(session =>
                session.Status == UserSessionStatuses.Active)));

        factory.AdvanceTime(TimeSpan.FromDays(7));
        var expired = await client.PostAsJsonAsync(
            "/api/auth/refresh",
            new RefreshTokenRequest { RefreshToken = login.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, expired.StatusCode);
        Assert.True(await factory.ExecuteDbContextAsync(dbContext =>
            dbContext.Set<UserSession>().AnyAsync(session =>
                session.Status == UserSessionStatuses.Expired)));
    }

    [Fact]
    public async Task LogoutAndRevoke_ImmediatelyInvalidateOwnedSessions()
    {
        using var factory = await CreateReadyFactoryAsync();
        using var alice = factory.CreateApiClient();
        await RegisterAsync(alice, TestRequests.ValidRegistration());
        var aliceLogin = await LoginAsync(alice, "alice");
        var aliceSessionId = ReadSessionId(aliceLogin.AccessToken);
        Authorize(alice, aliceLogin.AccessToken);

        var logout = await alice.PostAsync("/api/auth/logout", null);
        logout.EnsureSuccessStatusCode();
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await alice.GetAsync("/api/test-authorization/user")).StatusCode);

        alice.DefaultRequestHeaders.Authorization = null;
        var activeAlice = await LoginAsync(alice, "alice");
        Authorize(alice, activeAlice.AccessToken);

        using var bob = factory.CreateApiClient();
        await RegisterAsync(bob, new RegisterRequest
        {
            UserName = "bob",
            Password = TestRequests.Password,
            Phone = "13900000002",
            Email = "bob@example.com",
        });
        var bobLogin = await LoginAsync(bob, "bob");
        var bobSessionId = ReadSessionId(bobLogin.AccessToken);

        var foreignRevoke = await alice.DeleteAsync(
            $"/api/auth/sessions/{bobSessionId}");
        Assert.Equal(HttpStatusCode.NotFound, foreignRevoke.StatusCode);

        var historicalRevoke = await alice.DeleteAsync(
            $"/api/auth/sessions/{aliceSessionId}");
        historicalRevoke.EnsureSuccessStatusCode();

        var activeSessionId = ReadSessionId(activeAlice.AccessToken);
        var activeRevoke = await alice.DeleteAsync(
            $"/api/auth/sessions/{activeSessionId}");
        activeRevoke.EnsureSuccessStatusCode();
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await alice.GetAsync("/api/test-authorization/user")).StatusCode);
    }

    [Fact]
    public async Task LogoutAll_RevokesCurrentSessionAndWritesSecurityAudit()
    {
        using var factory = await CreateReadyFactoryAsync();
        using var client = factory.CreateApiClient();
        await RegisterAsync(client, TestRequests.ValidRegistration());
        var login = await LoginAsync(client, "alice");
        Authorize(client, login.AccessToken);

        var response = await client.PostAsync("/api/auth/logout-all", null);
        response.EnsureSuccessStatusCode();
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await client.GetAsync("/api/test-authorization/user")).StatusCode);

        var operationTypes = await factory.ExecuteDbContextAsync(dbContext =>
            dbContext.Set<OperationLog>()
                .AsNoTracking()
                .Select(log => log.OperationType)
                .ToListAsync());
        Assert.Contains("LOGIN", operationTypes);
        Assert.Contains("LOGOUT_ALL", operationTypes);
    }

    private static async Task<AuthTestFactory> CreateReadyFactoryAsync()
    {
        var factory = new AuthTestFactory();
        await factory.ResetDatabaseAsync();
        await factory.SeedRoleAsync();
        return factory;
    }

    private static async Task RegisterAsync(
        HttpClient client,
        RegisterRequest request)
    {
        var response = await client.PostAsJsonAsync("/api/auth/register", request);
        response.EnsureSuccessStatusCode();
    }

    private static async Task<LoginResponse> LoginAsync(
        HttpClient client,
        string account)
    {
        var response = await client.PostAsJsonAsync(
            "/api/auth/login",
            TestRequests.Login(account));
        response.EnsureSuccessStatusCode();
        return (await AuthTestFactory.ReadResponseAsync<LoginResponse>(response)).Data!;
    }

    private static void Authorize(HttpClient client, string accessToken) =>
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", accessToken);

    private static long ReadSessionId(string accessToken) =>
        long.Parse(new JwtSecurityTokenHandler()
            .ReadJwtToken(accessToken)
            .Claims
            .Single(claim => claim.Type == "sid")
            .Value);
}
