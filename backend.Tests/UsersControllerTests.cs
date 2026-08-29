using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using ShowtimeBackend.Common;
using ShowtimeBackend.DTOs.UserPermission;
using ShowtimeBackend.Entities.UserPermission;

namespace ShowtimeBackend.Tests;

/// <summary>
/// PUT /api/users/me/avatar：头像 URL 校验与持久化（AVATAR_URL 列）。
/// 覆盖未认证 401、非法 URL 400、成功写入 + 再次登录仍返回（刷新后仍显示）。
/// </summary>
public sealed class UsersControllerTests
{
    private const string AvatarPath = "/api/users/me/avatar";

    [Fact]
    public async Task UpdateAvatar_Anonymous_Returns401()
    {
        using var factory = new AuthTestFactory();
        using var client = factory.CreateApiClient();

        var response = await client.PutAsJsonAsync(
            AvatarPath,
            new { avatarUrl = "https://example.com/a.png" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("not-a-url")]
    [InlineData("ftp://example.com/a.png")]
    [InlineData("")]
    [InlineData("   ")]
    public async Task UpdateAvatar_InvalidUrl_Returns400InvalidAvatarUrl(string avatarUrl)
    {
        using var factory = new AuthTestFactory();
        using var client = await CreateAuthenticatedClientAsync(factory);

        var response = await client.PutAsJsonAsync(
            AvatarPath,
            new { avatarUrl });
        var envelope = await AuthTestFactory.ReadResponseAsync<UserResponse>(response);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("INVALID_AVATAR_URL", envelope.Code);
    }

    [Fact]
    public async Task UpdateAvatar_UrlLongerThan500Chars_Returns400InvalidAvatarUrl()
    {
        using var factory = new AuthTestFactory();
        using var client = await CreateAuthenticatedClientAsync(factory);

        var response = await client.PutAsJsonAsync(
            AvatarPath,
            new { avatarUrl = "https://example.com/" + new string('a', 500) });
        var envelope = await AuthTestFactory.ReadResponseAsync<UserResponse>(response);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("INVALID_AVATAR_URL", envelope.Code);
    }

    [Fact]
    public async Task UpdateAvatar_Success_PersistsAndSurvivesReLogin()
    {
        using var factory = new AuthTestFactory();
        using var client = await CreateAuthenticatedClientAsync(factory);
        const string avatarUrl = "https://showtime-assets.oss-cn-hangzhou.aliyuncs.com/showtime/avatar/2026/08/abc.png";

        var response = await client.PutAsJsonAsync(
            AvatarPath,
            new { avatarUrl });
        var envelope = await AuthTestFactory.ReadResponseAsync<UserResponse>(response);

        response.EnsureSuccessStatusCode();
        Assert.True(envelope.Success);
        Assert.Equal(avatarUrl, envelope.Data!.AvatarUrl);

        // 落库验证：DB 中的 SYS_USER.AVATAR_URL 已更新
        var persisted = await factory.ExecuteDbContextAsync(
            async dbContext => await dbContext.Set<SysUser>()
                .Where(user => user.UserName == "alice")
                .Select(user => user.AvatarUrl)
                .SingleAsync());
        Assert.Equal(avatarUrl, persisted);

        // 再次登录：返回的头像即持久化值（刷新后仍显示）
        var login = await client.PostAsJsonAsync(
            "/api/auth/login",
            TestRequests.Login("alice"));
        var loginEnvelope = await AuthTestFactory.ReadResponseAsync<LoginResponse>(login);
        Assert.Equal(avatarUrl, loginEnvelope.Data!.User.AvatarUrl);
    }

    [Fact]
    public async Task RegisterResponse_CarriesAvatarUrl()
    {
        using var factory = new AuthTestFactory();
        await factory.ResetDatabaseAsync();
        await factory.SeedRoleAsync();
        using var client = factory.CreateApiClient();

        var registration = await client.PostAsJsonAsync(
            "/api/auth/register",
            TestRequests.ValidRegistration());
        var envelope = await AuthTestFactory.ReadResponseAsync<RegisterResponse>(registration);

        registration.EnsureSuccessStatusCode();
        Assert.Null(envelope.Data!.User.AvatarUrl);
    }

    // ---------- helpers ----------

    private static async Task<HttpClient> CreateAuthenticatedClientAsync(
        AuthTestFactory factory)
    {
        await factory.ResetDatabaseAsync();
        await factory.SeedRoleAsync();
        var client = factory.CreateApiClient();

        var registration = await client.PostAsJsonAsync(
            "/api/auth/register",
            TestRequests.ValidRegistration());
        registration.EnsureSuccessStatusCode();

        var login = await client.PostAsJsonAsync(
            "/api/auth/login",
            TestRequests.Login("alice"));
        login.EnsureSuccessStatusCode();
        var loginEnvelope =
            await AuthTestFactory.ReadResponseAsync<LoginResponse>(login);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", loginEnvelope.Data!.AccessToken);
        return client;
    }
}
