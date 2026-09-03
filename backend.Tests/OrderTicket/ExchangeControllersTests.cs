using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace ShowtimeBackend.Tests.OrderTicket;

public sealed class ExchangeControllersTests
{
    [Theory]
    [InlineData("GET", "/api/orders/11/exchanges", null)]
    [InlineData("POST", "/api/orders/11/exchanges/quote", "{}")]
    [InlineData("POST", "/api/orders/11/exchanges", "{}")]
    [InlineData("GET", "/api/exchanges/1", null)]
    [InlineData("POST", "/api/exchanges/1/pay", "{}")]
    public async Task UserExchangeOperation_WithoutAuthentication_ReturnsUnauthorizedEnvelope(
        string method,
        string path,
        string? json)
    {
        using var factory = new AuthTestFactory();
        using var client = factory.CreateApiClient();
        using var request = CreateRequest(method, path, json);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await AuthTestFactory.ReadResponseAsync<object>(response);
        Assert.False(body.Success);
        Assert.Equal("AUTH_REQUIRED", body.Code);
    }

    [Theory]
    [InlineData("GET", "/api/admin/exchanges", null)]
    [InlineData("GET", "/api/admin/exchanges/1", null)]
    [InlineData("POST", "/api/admin/exchanges/1/approve", "{}")]
    [InlineData("POST", "/api/admin/exchanges/1/reject", "{}")]
    [InlineData("GET", "/api/admin/exchange-policies", null)]
    [InlineData("POST", "/api/admin/exchange-policies", "{}")]
    [InlineData("PUT", "/api/admin/exchange-policies/1", "{}")]
    [InlineData("PATCH", "/api/admin/exchange-policies/1/status", "{}")]
    public async Task AdminExchangeOperation_WithUserRole_ReturnsForbiddenEnvelope(
        string method,
        string path,
        string? json)
    {
        using var factory = new AuthTestFactory();
        using var client = factory.CreateApiClient();
        Authenticate(client, "USER");
        using var request = CreateRequest(method, path, json);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await AuthTestFactory.ReadResponseAsync<object>(response);
        Assert.False(body.Success);
        Assert.Equal("FORBIDDEN", body.Code);
    }

    [Theory]
    [InlineData("/api/orders/11/exchanges?approveStatus=1")]
    [InlineData("/api/orders/11/exchanges?exchangeStatus=2")]
    public async Task UserList_NumericStatus_ReturnsValidationEnvelope(string path)
    {
        using var factory = new AuthTestFactory();
        using var client = factory.CreateApiClient();
        Authenticate(client, "USER");

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await AuthTestFactory.ReadResponseAsync<object>(response);
        Assert.False(body.Success);
        Assert.Equal("VALIDATION_FAILED", body.Code);
    }

    [Fact]
    public async Task AdminList_NumericStatus_ReturnsValidationEnvelope()
    {
        using var factory = new AuthTestFactory();
        using var client = factory.CreateApiClient();
        Authenticate(client, "Admin");

        var response = await client.GetAsync(
            "/api/admin/exchanges?approveStatus=1");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await AuthTestFactory.ReadResponseAsync<object>(response);
        Assert.False(body.Success);
        Assert.Equal("VALIDATION_FAILED", body.Code);
    }

    [Fact]
    public async Task Pay_InvalidEnumString_ReturnsValidationEnvelope()
    {
        using var factory = new AuthTestFactory();
        using var client = factory.CreateApiClient();
        Authenticate(client, "USER");

        var response = await client.PostAsJsonAsync(
            "/api/exchanges/1/pay",
            new { payChannel = "INVALID", result = "SUCCESS" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await AuthTestFactory.ReadResponseAsync<object>(response);
        Assert.False(body.Success);
        Assert.Equal("VALIDATION_FAILED", body.Code);
    }

    private static HttpRequestMessage CreateRequest(
        string method,
        string path,
        string? json)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (json is not null)
            request.Content = JsonContent.Create(JsonDocument.Parse(json).RootElement);
        return request;
    }

    private static void Authenticate(HttpClient client, string role) =>
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            RefundTestData.CreateToken(7, "exchange-controller-user", role));
}
