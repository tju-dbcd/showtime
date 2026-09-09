using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using ShowtimeBackend.Common;
using ShowtimeBackend.Common.RateLimiting;
using ShowtimeBackend.DTOs.UserPermission;

namespace ShowtimeBackend.Tests.UserPermission;

public sealed class RateLimitingTests
{
    [Fact]
    public async Task Login_SixthRequestFromSameIpIsRejected()
    {
        using var factory = await CreateReadyFactoryAsync();
        using var client = factory.CreateApiClient();
        await RegisterAsync(client, TestRequests.ValidRegistration());

        for (var attempt = 1; attempt <= 5; attempt++)
        {
            var response = await client.PostAsJsonAsync(
                "/api/auth/login",
                TestRequests.Login("alice", "WrongPassword1"));
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        await AssertRateLimitedAsync(await client.PostAsJsonAsync(
            "/api/auth/login",
            TestRequests.Login("alice", "WrongPassword1")));
    }

    [Fact]
    public async Task Register_FourthRequestFromSameIpIsRejected()
    {
        using var factory = await CreateReadyFactoryAsync();
        using var client = factory.CreateApiClient();

        for (var index = 1; index <= 3; index++)
        {
            var response = await client.PostAsJsonAsync(
                "/api/auth/register",
                Registration($"user{index}", $"1390000000{index}"));
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }

        await AssertRateLimitedAsync(await client.PostAsJsonAsync(
            "/api/auth/register",
            Registration("user4", "13900000004")));
    }

    [Fact]
    public async Task Refresh_EleventhRequestFromSameIpIsRejected()
    {
        using var factory = await CreateReadyFactoryAsync();
        using var client = factory.CreateApiClient();
        var malformedToken = new string('x', 64);

        for (var attempt = 1; attempt <= 10; attempt++)
        {
            var response = await client.PostAsJsonAsync(
                "/api/auth/refresh",
                new RefreshTokenRequest { RefreshToken = malformedToken });
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        await AssertRateLimitedAsync(await client.PostAsJsonAsync(
            "/api/auth/refresh",
            new RefreshTokenRequest { RefreshToken = malformedToken }));
    }

    [Fact]
    public async Task AnonymousGeneralLimit_UsesUnifiedEnvelope()
    {
        using var factory = new AuthTestFactory(additionalConfiguration:
            new Dictionary<string, string?>
            {
                ["RateLimiting:AnonymousPerMinute"] = "3",
            });
        using var client = factory.CreateApiClient();

        for (var request = 1; request <= 3; request++)
        {
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/")).StatusCode);
        }

        await AssertRateLimitedAsync(await client.GetAsync("/"));
    }

    [Fact]
    public async Task AuthenticatedGeneralLimit_IsPartitionedByUser()
    {
        using var factory = await CreateReadyFactoryAsync(
            new Dictionary<string, string?>
            {
                ["RateLimiting:AuthenticatedPerMinute"] = "2",
            });
        using var alice = factory.CreateApiClient();
        await RegisterAsync(alice, TestRequests.ValidRegistration());
        Authorize(alice, (await LoginAsync(alice, "alice")).AccessToken);

        using var bob = factory.CreateApiClient();
        await RegisterAsync(bob, Registration("bob", "13900000002"));
        Authorize(bob, (await LoginAsync(bob, "bob")).AccessToken);

        Assert.Equal(
            HttpStatusCode.OK,
            (await alice.GetAsync("/api/test-authorization/user")).StatusCode);
        Assert.Equal(
            HttpStatusCode.OK,
            (await alice.GetAsync("/api/test-authorization/user")).StatusCode);
        await AssertRateLimitedAsync(
            await alice.GetAsync("/api/test-authorization/user"));

        Assert.Equal(
            HttpStatusCode.OK,
            (await bob.GetAsync("/api/test-authorization/user")).StatusCode);
    }

    [Fact]
    public async Task DedicatedAuthLimit_DoesNotConsumeAnonymousGeneralQuota()
    {
        using var factory = await CreateReadyFactoryAsync(
            new Dictionary<string, string?>
            {
                ["RateLimiting:LoginPerMinute"] = "1",
                ["RateLimiting:AnonymousPerMinute"] = "2",
            });
        using var client = factory.CreateApiClient();

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync(
                "/api/auth/login",
                TestRequests.Login("missing", "WrongPassword1"))).StatusCode);
        await AssertRateLimitedAsync(await client.PostAsJsonAsync(
            "/api/auth/login",
            TestRequests.Login("missing", "WrongPassword1")));

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/")).StatusCode);
        await AssertRateLimitedAsync(await client.GetAsync("/"));
    }

    [Fact]
    public void ConnectionKey_UsesIpAndFallsBackToConnectionId()
    {
        var first = new DefaultHttpContext();
        first.Connection.RemoteIpAddress = IPAddress.Parse("192.0.2.10");
        var second = new DefaultHttpContext();
        second.Connection.RemoteIpAddress = IPAddress.Parse("192.0.2.11");
        Assert.NotEqual(
            RateLimitingServiceExtensions.GetConnectionKey(first),
            RateLimitingServiceExtensions.GetConnectionKey(second));

        var noIpFirst = new DefaultHttpContext();
        noIpFirst.Connection.Id = "connection-a";
        var noIpSecond = new DefaultHttpContext();
        noIpSecond.Connection.Id = "connection-b";
        Assert.NotEqual(
            RateLimitingServiceExtensions.GetConnectionKey(noIpFirst),
            RateLimitingServiceExtensions.GetConnectionKey(noIpSecond));
    }

    [Fact]
    public void NonPositiveLimit_FailsApplicationStartupValidation()
    {
        using var factory = new AuthTestFactory(additionalConfiguration:
            new Dictionary<string, string?>
            {
                ["RateLimiting:LoginPerMinute"] = "0",
            });

        Assert.Throws<OptionsValidationException>(() => factory.CreateApiClient());
    }

    private static async Task AssertRateLimitedAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.NotNull(response.Headers.RetryAfter);
        Assert.True(response.Headers.RetryAfter!.Delta > TimeSpan.Zero);
        var envelope = await AuthTestFactory.ReadResponseAsync<object>(response);
        Assert.False(envelope.Success);
        Assert.Equal("RATE_LIMIT_EXCEEDED", envelope.Code);
    }

    private static async Task<AuthTestFactory> CreateReadyFactoryAsync(
        IReadOnlyDictionary<string, string?>? additionalConfiguration = null)
    {
        var factory = new AuthTestFactory(
            additionalConfiguration: additionalConfiguration);
        await factory.ResetDatabaseAsync();
        await factory.SeedRoleAsync();
        return factory;
    }

    private static RegisterRequest Registration(string userName, string phone) =>
        new()
        {
            UserName = userName,
            Password = TestRequests.Password,
            Phone = phone,
            Email = $"{userName}@example.com",
        };

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
}
