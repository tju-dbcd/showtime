using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using ShowtimeBackend.Services.OrderTicket.Messaging;

namespace ShowtimeBackend.Tests.OrderTicket;

public sealed class OrderSignalRTests
{
    [Fact]
    public void RabbitMqDisabledDoesNotRegisterPublisherOrConsumerWorkers()
    {
        using var factory = new AuthTestFactory();
        var workers = factory.Services.GetServices<IHostedService>();

        Assert.DoesNotContain(workers, worker => worker is OrderEventOutboxWorker);
        Assert.DoesNotContain(workers, worker => worker is RabbitMqOrderNotificationWorker);
        Assert.Null(factory.Services.GetService<IRabbitMqConnectionProvider>());
    }

    [Fact]
    public async Task HubNegotiateRejectsUnauthenticatedConnectionWithExistingEnvelope()
    {
        await using var factory = new AuthTestFactory();
        using var client = factory.CreateApiClient();

        var response = await client.PostAsync(
            "/hubs/order-notifications/negotiate?negotiateVersion=1",
            null);
        var body = await AuthTestFactory.ReadResponseAsync<object>(response);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("AUTH_REQUIRED", body.Code);
    }

    [Fact]
    public async Task QueryAccessTokenAuthenticatesOnlyHubPath()
    {
        await using var factory = new AuthTestFactory();
        using var client = factory.CreateApiClient();
        var token = CreateToken("7");

        var hub = await client.PostAsync(
            $"/hubs/order-notifications/negotiate?negotiateVersion=1&access_token={token}",
            null);
        var ordinaryApi = await client.GetAsync(
            $"/api/test-authorization/user?access_token={token}");

        Assert.Equal(HttpStatusCode.OK, hub.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, ordinaryApi.StatusCode);
    }

    [Fact]
    public void UserIdProviderUsesUnmappedSubjectClaim()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(JwtRegisteredClaimNames.Sub, "987")],
            "test"));

        Assert.Equal("987", SubjectUserIdProvider.GetUserId(principal));
    }

    [Fact]
    public async Task DispatcherTargetsOwningUserWithStableMethodName()
    {
        var clients = new RecordingHubClients();
        var dispatcher = new SignalROrderNotificationDispatcher(
            new TestHubContext(clients));
        var notification = new OrderCreatedEvent(
            Guid.NewGuid().ToString("D"),
            OrderCreatedEvent.TypeName,
            DateTime.UtcNow,
            101,
            "ORD101",
            7,
            10,
            100m,
            1,
            "PENDING_PAY");

        await dispatcher.DispatchOrderCreatedAsync(notification, CancellationToken.None);

        Assert.Equal("7", clients.UserId);
        Assert.Equal("OrderCreated", clients.Proxy.Method);
        Assert.Same(notification, Assert.Single(clients.Proxy.Arguments!));
    }

    private static string CreateToken(string userId)
    {
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(AuthTestFactory.TestKey)),
            SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            AuthTestFactory.TestIssuer,
            AuthTestFactory.TestAudience,
            [
                new Claim(JwtRegisteredClaimNames.Sub, userId),
                new Claim("role", "USER"),
            ],
            expires: DateTime.UtcNow.AddMinutes(10),
            signingCredentials: credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private sealed class TestHubContext(IHubClients clients) : IHubContext<OrderNotificationsHub>
    {
        public IHubClients Clients { get; } = clients;
        public IGroupManager Groups { get; } = null!;
    }

    private sealed class RecordingHubClients : IHubClients
    {
        public string? UserId { get; private set; }
        public RecordingClientProxy Proxy { get; } = new();
        public IClientProxy All => Proxy;
        public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => Proxy;
        public IClientProxy Client(string connectionId) => Proxy;
        public IClientProxy Clients(IReadOnlyList<string> connectionIds) => Proxy;
        public IClientProxy Group(string groupName) => Proxy;
        public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => Proxy;
        public IClientProxy Groups(IReadOnlyList<string> groupNames) => Proxy;
        public IClientProxy User(string userId)
        {
            UserId = userId;
            return Proxy;
        }
        public IClientProxy Users(IReadOnlyList<string> userIds) => Proxy;
    }

    private sealed class RecordingClientProxy : IClientProxy
    {
        public string? Method { get; private set; }
        public object?[]? Arguments { get; private set; }

        public Task SendCoreAsync(
            string method,
            object?[] args,
            CancellationToken cancellationToken = default)
        {
            Method = method;
            Arguments = args;
            return Task.CompletedTask;
        }
    }
}
