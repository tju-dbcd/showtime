using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ShowtimeBackend.DTOs.UserPermission;
using ShowtimeBackend.Entities.UserPermission;

namespace ShowtimeBackend.Tests;

public sealed class RegistrationTests
{
    [Fact]
    public async Task Register_CreatesUserAndDefaultRole_WithHashedPassword()
    {
        using var factory = new AuthTestFactory();
        await factory.ResetDatabaseAsync();
        await factory.SeedRoleAsync();
        using var client = factory.CreateApiClient();
        var request = TestRequests.ValidRegistration();

        var httpResponse = await client.PostAsJsonAsync("/api/auth/register", request);

        Assert.Equal(HttpStatusCode.Created, httpResponse.StatusCode);
        var response = await AuthTestFactory.ReadResponseAsync<RegisterResponse>(httpResponse);
        Assert.True(response.Success);
        Assert.Null(response.Code);
        Assert.Equal("alice", response.Data!.User.UserName);
        Assert.Equal(["USER"], response.Data.User.Roles);

        var user = await factory.ExecuteDbContextAsync(dbContext =>
            dbContext.Set<SysUser>()
                .AsNoTracking()
                .Include(item => item.UserRoles)
                .ThenInclude(item => item.Role)
                .SingleAsync());
        Assert.NotEqual(TestRequests.Password, user.PasswordHash);
        Assert.Equal(
            PasswordVerificationResult.Success,
            new PasswordHasher<SysUser>().VerifyHashedPassword(
                user,
                user.PasswordHash,
                TestRequests.Password));
        Assert.Equal("USER", Assert.Single(user.UserRoles).Role.RoleCode);
    }

    [Theory]
    [InlineData("username", "AUTH_USERNAME_TAKEN")]
    [InlineData("phone", "AUTH_PHONE_TAKEN")]
    [InlineData("email", "AUTH_EMAIL_TAKEN")]
    public async Task Register_ReturnsConflict_ForDuplicateFields(
        string duplicateField,
        string expectedCode)
    {
        using var factory = new AuthTestFactory();
        await factory.ResetDatabaseAsync();
        await factory.SeedRoleAsync();
        using var client = factory.CreateApiClient();
        var original = TestRequests.ValidRegistration();
        var firstResponse = await client.PostAsJsonAsync("/api/auth/register", original);
        firstResponse.EnsureSuccessStatusCode();

        var duplicate = duplicateField switch
        {
            "username" => original with
            {
                Phone = "+8613900139000",
                Email = "second@example.com",
            },
            "phone" => original with
            {
                UserName = "second",
                Email = "second@example.com",
            },
            "email" => original with
            {
                UserName = "second",
                Phone = "+8613900139000",
                Email = "ALICE@EXAMPLE.COM",
            },
            _ => throw new ArgumentOutOfRangeException(nameof(duplicateField)),
        };

        var httpResponse = await client.PostAsJsonAsync("/api/auth/register", duplicate);

        Assert.Equal(HttpStatusCode.Conflict, httpResponse.StatusCode);
        var response = await AuthTestFactory.ReadResponseAsync<RegisterResponse>(httpResponse);
        Assert.False(response.Success);
        Assert.Equal(expectedCode, response.Code);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Register_ReturnsServiceUnavailable_WhenDefaultRoleIsUnavailable(
        bool seedDisabledRole)
    {
        using var factory = new AuthTestFactory();
        await factory.ResetDatabaseAsync();
        if (seedDisabledRole)
        {
            await factory.SeedRoleAsync(status: false);
        }

        using var client = factory.CreateApiClient();
        var httpResponse = await client.PostAsJsonAsync(
            "/api/auth/register",
            TestRequests.ValidRegistration());

        Assert.Equal(HttpStatusCode.ServiceUnavailable, httpResponse.StatusCode);
        var response = await AuthTestFactory.ReadResponseAsync<RegisterResponse>(httpResponse);
        Assert.Equal("AUTH_DEFAULT_ROLE_UNAVAILABLE", response.Code);
        var userCount = await factory.ExecuteDbContextAsync(
            dbContext => dbContext.Set<SysUser>().CountAsync());
        Assert.Equal(0, userCount);
    }

    [Fact]
    public async Task Register_ReturnsUnifiedValidationResponse_ForWeakPassword()
    {
        using var factory = new AuthTestFactory();
        await factory.ResetDatabaseAsync();
        await factory.SeedRoleAsync();
        using var client = factory.CreateApiClient();
        var request = TestRequests.ValidRegistration() with { Password = "weakpass" };

        var httpResponse = await client.PostAsJsonAsync("/api/auth/register", request);

        Assert.Equal(HttpStatusCode.BadRequest, httpResponse.StatusCode);
        var response = await AuthTestFactory.ReadResponseAsync<object>(httpResponse);
        Assert.False(response.Success);
        Assert.Equal("VALIDATION_FAILED", response.Code);
        var userCount = await factory.ExecuteDbContextAsync(
            dbContext => dbContext.Set<SysUser>().CountAsync());
        Assert.Equal(0, userCount);
    }
}
