using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ShowtimeBackend.DTOs.Auth;

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
        var expectedExpiry = factory.UtcNow.UtcDateTime.AddHours(2);
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
}
