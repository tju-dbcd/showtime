using System.Reflection;
using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using ShowtimeBackend.Common;
using ShowtimeBackend.Common.TicketSecurity;
using ShowtimeBackend.Controllers.OrderTicket;
using ShowtimeBackend.DTOs.OrderTicket;
using ShowtimeBackend.Entities.OrderTicket;
using ShowtimeBackend.Services.OrderTicket;
using ShowSessionEntity = ShowtimeBackend.Entities.ShowSession.ShowSession;

namespace ShowtimeBackend.Tests.OrderTicket;

public sealed class AdminTicketsControllerTests
{
    [Fact]
    public void Controller_UsesAdminOnlyTicketsRoute()
    {
        var type = typeof(AdminTicketsController);

        Assert.Equal("api/admin/tickets", type.GetCustomAttribute<RouteAttribute>()!.Template);
        Assert.Equal("Admin", type.GetCustomAttribute<AuthorizeAttribute>()!.Roles);
        var action = type.GetMethod(nameof(AdminTicketsController.Redeem))!;
        Assert.Equal("redeem", action.GetCustomAttribute<HttpPostAttribute>()!.Template);
    }

    [Fact]
    public async Task Redeem_UsesCurrentActorAndReturnsSuccessEnvelope()
    {
        var response = new TicketRedemptionResponse(
            201,
            "TKT201",
            11,
            101,
            21,
            ETicketStatus.USED,
            new DateTime(2026, 8, 28, 12, 0, 0, DateTimeKind.Utc),
            "gate",
            "admin-user");
        var service = new StubService(OrderTicketResult<TicketRedemptionResponse>.Success(response));
        var controller = CreateController(service, authenticated: true);
        var request = new RedeemTicketRequest("qr", "gate");

        var result = await controller.Redeem(request, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var envelope = Assert.IsType<ApiResponse<TicketRedemptionResponse>>(ok.Value);
        Assert.Equal(201, envelope.Data!.ETicketId);
        Assert.Equal("admin-user", service.Actor);
        Assert.Same(request, service.Request);
    }

    [Fact]
    public async Task Redeem_WhenSubjectIsMissing_ReturnsUnauthorizedWithoutCallingService()
    {
        var service = new StubService(
            OrderTicketResult<TicketRedemptionResponse>.Fail(
                OrderTicketFailure.Internal,
                "UNUSED",
                "unused"));
        var controller = CreateController(service, authenticated: false);

        var result = await controller.Redeem(
            new RedeemTicketRequest("qr", "gate"),
            CancellationToken.None);

        Assert.IsType<UnauthorizedObjectResult>(result.Result);
        Assert.Null(service.Actor);
    }

    [Fact]
    public async Task RedeemEndpoint_RequiresAuthenticationAndAdminRole()
    {
        using var factory = new AuthTestFactory();
        using var anonymous = factory.CreateApiClient();

        var unauthorized = await anonymous.PostAsJsonAsync(
            "/api/admin/tickets/redeem",
            new { qrCode = "invalid", checkDevice = "gate" });

        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        using var userClient = factory.CreateApiClient();
        userClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            CreateAccessToken("User"));
        var forbidden = await userClient.PostAsJsonAsync(
            "/api/admin/tickets/redeem",
            new { qrCode = "invalid", checkDevice = "gate" });
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }

    [Theory]
    [InlineData(null, "gate", "TICKET_QR_INVALID")]
    [InlineData("", "gate", "TICKET_QR_INVALID")]
    [InlineData("   ", "gate", "TICKET_QR_INVALID")]
    [InlineData("invalid", null, "TICKET_QR_INVALID")]
    public async Task RedeemEndpoint_FieldValidationUsesDomainErrors(
        string? qrCode,
        string? checkDevice,
        string expectedCode)
    {
        using var factory = new AuthTestFactory();
        using var client = CreateAdminClient(factory);

        var response = await client.PostAsJsonAsync(
            "/api/admin/tickets/redeem",
            new { qrCode, checkDevice });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var envelope = await AuthTestFactory.ReadResponseAsync<TicketRedemptionResponse>(
            response);
        Assert.Equal(expectedCode, envelope.Code);
    }

    [Fact]
    public async Task RedeemEndpoint_ValidQrWithMissingDeviceReturnsDeviceError()
    {
        using var factory = new AuthTestFactory();
        using var client = CreateAdminClient(factory);
        var qrCode = CreateTicketTokenService().Generate(DateTimeOffset.UtcNow).QrCode;

        var response = await client.PostAsJsonAsync(
            "/api/admin/tickets/redeem",
            new { qrCode, checkDevice = (string?)null });

        var envelope = await AuthTestFactory.ReadResponseAsync<TicketRedemptionResponse>(
            response);
        Assert.Equal("TICKET_DEVICE_INVALID", envelope.Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{")]
    public async Task RedeemEndpoint_InvalidJsonTransportReturnsGlobalValidationError(
        string body)
    {
        using var factory = new AuthTestFactory();
        using var client = CreateAdminClient(factory);
        using var content = new StringContent(body, Encoding.UTF8, "application/json");

        var response = await client.PostAsync("/api/admin/tickets/redeem", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var envelope = await AuthTestFactory.ReadResponseAsync<TicketRedemptionResponse>(
            response);
        Assert.Equal("VALIDATION_FAILED", envelope.Code);
    }

    [Fact]
    public async Task RedeemEndpoint_SuccessReturnsUtcTimestampWithZ()
    {
        using var factory = new AuthTestFactory();
        await factory.ResetDatabaseAsync();
        var credential = CreateTicketTokenService().Generate(factory.UtcNow);
        await factory.ExecuteDbContextAsync(async db =>
        {
            await db.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = OFF;");
            db.AddRange(
                new ShowSessionEntity
                {
                    SessionId = 21,
                    ShowId = 90,
                    SeatMapId = 30,
                    StartTime = factory.UtcNow.UtcDateTime.AddHours(-1),
                    EndTime = factory.UtcNow.UtcDateTime.AddHours(1),
                    SaleStartTime = factory.UtcNow.UtcDateTime.AddDays(-10),
                    SaleEndTime = factory.UtcNow.UtcDateTime.AddDays(-1),
                    SessionStatus = "ENDED",
                },
                new Order
                {
                    OrderId = 11,
                    OrderNo = "ORD000011",
                    UserId = 7,
                    SessionId = 21,
                    TotalAmount = 188m,
                    TicketCount = 1,
                    OrderStatus = "ISSUED",
                    ExpireTime = factory.UtcNow.UtcDateTime.AddDays(-1),
                    IssueTime = factory.UtcNow.UtcDateTime.AddDays(-1),
                    Source = "WEB",
                },
                new OrderItem
                {
                    OrderItemId = 101,
                    OrderId = 11,
                    SeatId = 501,
                    PriceStrategyId = 601,
                    UnitPrice = 188m,
                    ItemStatus = "NORMAL",
                },
                new ETicket
                {
                    ETicketId = 201,
                    ETicketNo = credential.TicketNo,
                    OrderItemId = 101,
                    UserId = 7,
                    QrCode = credential.QrCode,
                    AntiFakeCode = credential.AntiFakeCode,
                    TicketStatus = "UNUSED",
                });
            await db.SaveChangesAsync();
            return true;
        });
        using var client = CreateAdminClient(factory);

        var response = await client.PostAsJsonAsync(
            "/api/admin/tickets/redeem",
            new { qrCode = credential.QrCode, checkDevice = "gate" });

        response.EnsureSuccessStatusCode();
        var raw = await response.Content.ReadAsStringAsync();
        Assert.Contains("Z\"", raw, StringComparison.Ordinal);
        using var document = JsonDocument.Parse(raw);
        var data = document.RootElement.GetProperty("data");
        Assert.Equal("USED", data.GetProperty("ticketStatus").GetString());
        Assert.Equal(DateTimeKind.Utc, data.GetProperty("checkTime").GetDateTime().Kind);
    }

    private static AdminTicketsController CreateController(
        StubService service,
        bool authenticated)
    {
        var claims = authenticated
            ? new[]
            {
                new Claim("sub", "1001"),
                new Claim(ClaimTypes.Name, "admin-user"),
                new Claim(ClaimTypes.Role, "Admin"),
            }
            : [];
        var identity = new ClaimsIdentity(claims, authenticated ? "test" : null);
        return new AdminTicketsController(service)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(identity),
                },
            },
        };
    }

    private static HttpClient CreateAdminClient(AuthTestFactory factory)
    {
        var client = factory.CreateApiClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            CreateAccessToken("Admin"));
        return client;
    }

    private static string CreateAccessToken(string role)
    {
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(AuthTestFactory.TestKey)),
            SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            AuthTestFactory.TestIssuer,
            AuthTestFactory.TestAudience,
            [
                new Claim(JwtRegisteredClaimNames.Sub, "1001"),
                new Claim(JwtRegisteredClaimNames.UniqueName, "admin-user"),
                new Claim("role", role),
            ],
            expires: DateTime.UtcNow.AddMinutes(10),
            signingCredentials: credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static ITicketTokenService CreateTicketTokenService() =>
        new HmacTicketTokenService(
            Microsoft.Extensions.Options.Options.Create(
                new TicketSecurityOptions
                {
                    SigningKeyBase64 =
                        "ERERERERERERERERERERERERERERERERERERERERERE=",
                }));

    private sealed class StubService(OrderTicketResult<TicketRedemptionResponse> result)
        : ITicketRedemptionService
    {
        public string? Actor { get; private set; }
        public RedeemTicketRequest? Request { get; private set; }

        public Task<OrderTicketResult<TicketRedemptionResponse>> RedeemAsync(
            string actor,
            RedeemTicketRequest request,
            CancellationToken cancellationToken)
        {
            Actor = actor;
            Request = request;
            return Task.FromResult(result);
        }
    }
}
