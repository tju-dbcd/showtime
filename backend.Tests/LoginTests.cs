using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using ShowtimeBackend.DTOs.UserPermission;
using ShowtimeBackend.Entities.UserPermission;

namespace ShowtimeBackend.Tests;

public sealed class LoginTests
{
    [Theory]
    [InlineData("alice")]
    [InlineData("+8613800138000")]
    [InlineData("ALICE@EXAMPLE.COM")]
    public async Task Login_AcceptsUserNamePhoneOrEmail(string account)
    {
        using var factory = new AuthTestFactory();
        await RegisterUserAsync(factory);
        using var client = factory.CreateApiClient();

        var httpResponse = await client.PostAsJsonAsync(
            "/api/auth/login",
            TestRequests.Login(account));

        Assert.Equal(HttpStatusCode.OK, httpResponse.StatusCode);
        var response = await AuthTestFactory.ReadResponseAsync<LoginResponse>(httpResponse);
        Assert.True(response.Success);
        Assert.Equal("Bearer", response.Data!.TokenType);
        Assert.Equal(900, response.Data.ExpiresIn);
        Assert.Equal(["USER"], response.Data.User.Roles);
        Assert.False(string.IsNullOrWhiteSpace(response.Data.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(response.Data.RefreshToken));
    }

    [Fact]
    public async Task Login_UsesSameResponse_ForUnknownAccountAndWrongPassword()
    {
        using var factory = new AuthTestFactory();
        await RegisterUserAsync(factory);
        using var client = factory.CreateApiClient();

        var unknownHttpResponse = await client.PostAsJsonAsync(
            "/api/auth/login",
            TestRequests.Login("nobody"));
        var wrongPasswordHttpResponse = await client.PostAsJsonAsync(
            "/api/auth/login",
            TestRequests.Login("alice", "Wrong123"));

        Assert.Equal(HttpStatusCode.Unauthorized, unknownHttpResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, wrongPasswordHttpResponse.StatusCode);
        Assert.Equal(
            await unknownHttpResponse.Content.ReadAsStringAsync(),
            await wrongPasswordHttpResponse.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData(0, "AUTH_ACCOUNT_DISABLED")]
    [InlineData(2, "AUTH_ACCOUNT_LOCKED")]
    public async Task Login_RejectsUnavailableAccount(byte status, string expectedCode)
    {
        using var factory = new AuthTestFactory();
        await RegisterUserAsync(factory);
        await factory.ExecuteDbContextAsync(async dbContext =>
        {
            var user = await dbContext.Set<SysUser>().SingleAsync();
            user.Status = status;
            await dbContext.SaveChangesAsync();
            return true;
        });
        using var client = factory.CreateApiClient();

        var httpResponse = await client.PostAsJsonAsync(
            "/api/auth/login",
            TestRequests.Login("alice"));

        Assert.Equal(HttpStatusCode.Forbidden, httpResponse.StatusCode);
        var response = await AuthTestFactory.ReadResponseAsync<LoginResponse>(httpResponse);
        Assert.Equal(expectedCode, response.Code);
        Assert.Null(response.Data);
    }

    [Fact]
    public async Task Login_IncludesOnlyDistinctEnabledRoles_InStableOrder()
    {
        using var factory = new AuthTestFactory();
        await RegisterUserAsync(factory);
        var adminRole = await factory.SeedRoleAsync("ADMIN");
        var disabledRole = await factory.SeedRoleAsync("OLD_ROLE", status: false);
        await factory.ExecuteDbContextAsync(async dbContext =>
        {
            var userId = await dbContext.Set<SysUser>()
                .Select(user => user.UserId)
                .SingleAsync();
            dbContext.AddRange(
                new UserRole { UserId = userId, RoleId = adminRole.RoleId },
                new UserRole { UserId = userId, RoleId = disabledRole.RoleId });
            await dbContext.SaveChangesAsync();
            return true;
        });
        using var client = factory.CreateApiClient();

        var httpResponse = await client.PostAsJsonAsync(
            "/api/auth/login",
            TestRequests.Login("alice"));

        var response = await AuthTestFactory.ReadResponseAsync<LoginResponse>(httpResponse);
        Assert.Equal(["ADMIN", "USER"], response.Data!.User.Roles);
    }

    private static async Task RegisterUserAsync(AuthTestFactory factory)
    {
        await factory.ResetDatabaseAsync();
        await factory.SeedRoleAsync();
        using var client = factory.CreateApiClient();
        var response = await client.PostAsJsonAsync(
            "/api/auth/register",
            TestRequests.ValidRegistration());
        response.EnsureSuccessStatusCode();
    }
}
