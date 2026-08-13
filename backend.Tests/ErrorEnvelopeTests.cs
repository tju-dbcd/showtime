using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using ShowtimeBackend.Common;
using ShowtimeBackend.DTOs.UserPermission;
using ShowtimeBackend.Entities.UserPermission;

namespace ShowtimeBackend.Tests;

/// <summary>
/// 验证统一错误响应格式：401/403/500 均返回 ApiResponse 信封，
/// 与业务错误（ApiResponse.Fail）保持一致，前端只需解析一种格式。
/// </summary>
public sealed class ErrorEnvelopeTests
{
    [Fact]
    public async Task AnonymousRequest_Returns401_WithApiResponseEnvelope()
    {
        using var factory = new AuthTestFactory();
        using var client = factory.CreateApiClient();

        var response = await client.GetAsync("/api/test-authorization/user");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var apiResponse = await AuthTestFactory.ReadResponseAsync<object>(response);
        Assert.False(apiResponse.Success);
        Assert.Equal("AUTH_REQUIRED", apiResponse.Code);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task UserRoleOnAdminEndpoint_Returns403_WithApiResponseEnvelope()
    {
        using var factory = new AuthTestFactory();
        await factory.ResetDatabaseAsync();
        await factory.SeedRoleAsync(); // USER
        using (var client = factory.CreateApiClient())
        {
            var registration = await client.PostAsJsonAsync(
                "/api/auth/register",
                TestRequests.ValidRegistration());
            registration.EnsureSuccessStatusCode();
        }
        using (var loginClient = factory.CreateApiClient())
        {
            var login = await loginClient.PostAsJsonAsync(
                "/api/auth/login",
                TestRequests.Login("alice"));
            login.EnsureSuccessStatusCode();
            var loginResponse = await AuthTestFactory.ReadResponseAsync<LoginResponse>(login);
            using var client = factory.CreateApiClient();
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", loginResponse.Data!.AccessToken);

            var response = await client.GetAsync("/api/admin/shows");

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            var apiResponse = await AuthTestFactory.ReadResponseAsync<object>(response);
            Assert.False(apiResponse.Success);
            Assert.Equal("FORBIDDEN", apiResponse.Code);
        }
    }

    [Fact]
    public async Task UnhandledException_Returns500_WithApiResponseEnvelope()
    {
        using var factory = new AuthTestFactory();
        await factory.ResetDatabaseAsync();
        await factory.SeedRoleAsync(); // USER
        using (var client = factory.CreateApiClient())
        {
            var registration = await client.PostAsJsonAsync(
                "/api/auth/register",
                TestRequests.ValidRegistration());
            registration.EnsureSuccessStatusCode();
        }
        using (var loginClient = factory.CreateApiClient())
        {
            var login = await loginClient.PostAsJsonAsync(
                "/api/auth/login",
                TestRequests.Login("alice"));
            login.EnsureSuccessStatusCode();
            var loginResponse = await AuthTestFactory.ReadResponseAsync<LoginResponse>(login);
            using var client = factory.CreateApiClient();
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", loginResponse.Data!.AccessToken);

            var response = await client.GetAsync("/api/test-authorization/boom");

            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            var apiResponse = await AuthTestFactory.ReadResponseAsync<object>(response);
            Assert.False(apiResponse.Success);
            Assert.Equal("INTERNAL_ERROR", apiResponse.Code);
        }
    }
}
