using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using ShowtimeBackend.DTOs.UserPermission;

namespace ShowtimeBackend.Tests;

public sealed class JwtAuthenticationTests
{
    [Fact]
    public async Task LoginToken_ContainsRequiredClaims_AndAuthorizesUserRole()
    {
        using var factory = new AuthTestFactory();
        await factory.ResetDatabaseAsync();
        await factory.SeedRoleAsync();
        using var client = factory.CreateApiClient();
        var registration = await client.PostAsJsonAsync(
            "/api/auth/register",
            TestRequests.ValidRegistration());
        registration.EnsureSuccessStatusCode();

        var login = await client.PostAsJsonAsync(
            "/api/auth/login",
            TestRequests.Login("alice"));
        login.EnsureSuccessStatusCode();
        var response = await AuthTestFactory.ReadResponseAsync<LoginResponse>(login);
        var accessToken = response.Data!.AccessToken;

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(accessToken);
        Assert.Equal(AuthTestFactory.TestIssuer, jwt.Issuer);
        Assert.Contains(AuthTestFactory.TestAudience, jwt.Audiences);
        Assert.Equal(
            response.Data.User.UserId.ToString(),
            jwt.Claims.Single(claim => claim.Type == JwtRegisteredClaimNames.Sub).Value);
        Assert.Equal(
            "alice",
            jwt.Claims.Single(claim => claim.Type == JwtRegisteredClaimNames.UniqueName).Value);
        Assert.Equal(
            "USER",
            jwt.Claims.Single(claim => claim.Type == "role").Value);
        var sessionId = await factory.ExecuteDbContextAsync(async dbContext =>
            await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
                .SingleAsync(dbContext.Set<ShowtimeBackend.Entities.UserPermission.UserSession>()));
        Assert.Equal(
            sessionId.UserSessionId.ToString(),
            jwt.Claims.Single(claim => claim.Type == "sid").Value);
        Assert.False(string.IsNullOrWhiteSpace(response.Data.RefreshToken));
        Assert.Equal(
            factory.UtcNow.UtcDateTime.AddDays(7),
            response.Data.RefreshTokenExpiresAtUtc);
        var expectedExpiry = factory.UtcNow.UtcDateTime.AddMinutes(15);
        expectedExpiry = expectedExpiry.AddTicks(
            -(expectedExpiry.Ticks % TimeSpan.TicksPerSecond));
        Assert.Equal(expectedExpiry, jwt.ValidTo);

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", accessToken);
        var authorized = await client.GetAsync("/api/test-authorization/user");

        Assert.Equal(HttpStatusCode.OK, authorized.StatusCode);
    }

    [Fact]
    public async Task UserRoleEndpoint_RejectsAnonymousRequests()
    {
        using var factory = new AuthTestFactory();
        using var client = factory.CreateApiClient();

        var response = await client.GetAsync("/api/test-authorization/user");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AccessTokenWithoutSessionClaim_IsRejected()
    {
        using var factory = new AuthTestFactory();
        using var client = factory.CreateApiClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            CreateAccessToken(factory.UtcNow, "7", null));

        var response = await client.GetAsync("/api/test-authorization/user");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var envelope = await AuthTestFactory.ReadResponseAsync<object>(response);
        Assert.Equal("AUTH_REQUIRED", envelope.Code);
    }

    [Fact]
    public async Task SessionOwnedByDifferentUser_IsRejected()
    {
        using var factory = new AuthTestFactory();
        await factory.ResetDatabaseAsync();
        await factory.SeedRoleAsync();
        using var client = factory.CreateApiClient();
        (await client.PostAsJsonAsync(
            "/api/auth/register",
            TestRequests.ValidRegistration())).EnsureSuccessStatusCode();
        var login = await client.PostAsJsonAsync(
            "/api/auth/login",
            TestRequests.Login("alice"));
        login.EnsureSuccessStatusCode();
        var loginBody = await AuthTestFactory.ReadResponseAsync<LoginResponse>(login);
        var sessionId = new JwtSecurityTokenHandler()
            .ReadJwtToken(loginBody.Data!.AccessToken)
            .Claims
            .Single(claim => claim.Type == "sid")
            .Value;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            CreateAccessToken(factory.UtcNow, "999", sessionId));

        var response = await client.GetAsync("/api/test-authorization/user");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static string CreateAccessToken(
        DateTimeOffset now,
        string userId,
        string? sessionId)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId),
            new(JwtRegisteredClaimNames.UniqueName, "test-user"),
            new("role", "USER"),
        };
        if (sessionId is not null)
        {
            claims.Add(new Claim("sid", sessionId));
        }

        var token = new JwtSecurityToken(
            AuthTestFactory.TestIssuer,
            AuthTestFactory.TestAudience,
            claims,
            now.UtcDateTime,
            now.AddMinutes(15).UtcDateTime,
            new SigningCredentials(
                new SymmetricSecurityKey(
                    Encoding.UTF8.GetBytes(AuthTestFactory.TestKey)),
                SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
