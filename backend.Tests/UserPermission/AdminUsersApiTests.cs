using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using ShowtimeBackend.Common;
using ShowtimeBackend.DTOs.UserPermission;
using ShowtimeBackend.Entities.UserPermission;

namespace ShowtimeBackend.Tests.UserPermission;

/// <summary>
/// /api/admin/users 用户管理接口：分页查询、创建（默认 USER 角色）、删除
/// （仅无订单/票务/会话/实名等关联数据时可物理删除）。
/// </summary>
public sealed class AdminUsersApiTests
{
    private const string AdminUsersPath = "/api/admin/users";

    [Fact]
    public async Task List_Anonymous_Returns401()
    {
        using var factory = new AuthTestFactory();
        using var client = factory.CreateApiClient();

        var response = await client.GetAsync(AdminUsersPath);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task List_NonAdminUser_Returns403()
    {
        using var factory = new AuthTestFactory();
        using var client = await CreateAuthenticatedClientAsync(
            factory,
            grantAdminRole: false);

        var response = await client.GetAsync(AdminUsersPath);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Create_ReturnsCreatedUser_WithDefaultUserRole()
    {
        using var factory = new AuthTestFactory();
        using var client = await CreateAuthenticatedClientAsync(factory);

        var response = await client.PostAsJsonAsync(
            AdminUsersPath,
            new
            {
                userName = "bob",
                password = "Secure123",
                phone = "+8613900000000",
                nickname = "Bob",
                email = "bob@example.com",
            });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var envelope = await AuthTestFactory.ReadResponseAsync<AdminUserResponse>(response);
        Assert.True(envelope.Success);
        Assert.Equal("bob", envelope.Data!.UserName);
        Assert.Equal("Bob", envelope.Data.Nickname);
        Assert.Equal("NORMAL", envelope.Data.UserType);
        Assert.Equal(1, envelope.Data.Status);
        Assert.Equal(["USER"], envelope.Data.Roles);

        var persisted = await factory.ExecuteDbContextAsync(
            async dbContext => await dbContext.Set<SysUser>()
                .Where(user => user.UserName == "bob")
                .Select(user => new
                {
                    user.CreateBy,
                    user.UserRoles.Count,
                    RoleCodes = user.UserRoles
                        .Select(userRole => userRole.Role.RoleCode),
                })
                .SingleAsync());
        Assert.Equal("alice", persisted.CreateBy);
        Assert.Equal(1, persisted.Count);
        Assert.Equal(["USER"], persisted.RoleCodes);
    }

    [Fact]
    public async Task Create_DuplicateUserName_Returns409()
    {
        using var factory = new AuthTestFactory();
        using var client = await CreateAuthenticatedClientAsync(factory);
        await client.PostAsJsonAsync(
            AdminUsersPath,
            new
            {
                userName = "bob",
                password = "Secure123",
                phone = "+8613900000000",
            });

        var response = await client.PostAsJsonAsync(
            AdminUsersPath,
            new
            {
                userName = "bob",
                password = "Secure123",
                phone = "+8613900000001",
            });
        var envelope = await AuthTestFactory.ReadResponseAsync<AdminUserResponse>(response);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("AUTH_USERNAME_TAKEN", envelope.Code);
    }

    [Fact]
    public async Task Create_InvalidBody_Returns400()
    {
        using var factory = new AuthTestFactory();
        using var client = await CreateAuthenticatedClientAsync(factory);

        var response = await client.PostAsJsonAsync(
            AdminUsersPath,
            new { userName = "x", password = "123", phone = "123" });
        var envelope = await AuthTestFactory.ReadResponseAsync<object>(response);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("VALIDATION_FAILED", envelope.Code);
    }

    [Fact]
    public async Task List_FiltersByKeywordAndStatus()
    {
        using var factory = new AuthTestFactory();
        using var client = await CreateAuthenticatedClientAsync(factory);
        await CreateUserAsync(client, "bob", "+8613900000000");
        await CreateUserAsync(client, "carol", "+8613900000001");

        var all = await client.GetFromJsonAsync<ApiResponse<PagedResponse<AdminUserResponse>>>(
            AdminUsersPath);
        Assert.NotNull(all);
        Assert.True(all.Success);
        Assert.True(all.Data!.TotalCount >= 3, "管理员 + 两个新建用户应至少 3 条。");
        Assert.Equal(10, all.Data.PageSize);

        var keyword = await client.GetFromJsonAsync<ApiResponse<PagedResponse<AdminUserResponse>>>(
            $"{AdminUsersPath}?Keyword=carol&PageIndex=1&PageSize=10");
        Assert.NotNull(keyword);
        Assert.Equal(1, keyword.Data!.TotalCount);
        Assert.Equal("carol", keyword.Data.Items[0].UserName);

        var statusFiltered = await client.GetFromJsonAsync<
            ApiResponse<PagedResponse<AdminUserResponse>>>(
            $"{AdminUsersPath}?Status=1&PageIndex=1&PageSize=10");
        Assert.NotNull(statusFiltered);
        Assert.True(statusFiltered.Success);
        Assert.All(statusFiltered.Data!.Items, item => Assert.Equal(1, item.Status));
    }

    [Fact]
    public async Task Delete_UserWithoutRelatedData_RemovesUserAndRole()
    {
        using var factory = new AuthTestFactory();
        using var client = await CreateAuthenticatedClientAsync(factory);
        var created = await CreateUserAsync(client, "bob", "+8613900000000");

        var response = await client.DeleteAsync(
            $"{AdminUsersPath}/{created.UserId}");
        var envelope = await AuthTestFactory.ReadResponseAsync<bool>(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(envelope.Success);
        Assert.True(envelope.Data);

        var exists = await factory.ExecuteDbContextAsync(
            async dbContext => await dbContext.Set<SysUser>()
                .AnyAsync(user => user.UserName == "bob"));
        Assert.False(exists);

        var rolesLeft = await factory.ExecuteDbContextAsync(
            async dbContext => await dbContext.Set<UserRole>()
                .CountAsync(userRole => userRole.UserId == created.UserId));
        Assert.Equal(0, rolesLeft);
    }

    [Fact]
    public async Task Delete_UserWithSessionOrLogs_Returns409()
    {
        using var factory = new AuthTestFactory();
        using var client = await CreateAuthenticatedClientAsync(factory);
        var created = await CreateUserAsync(client, "bob", "+8613900000000");

        // 用户登录一次产生会话与操作日志，构成外键关联数据
        using var userClient = factory.CreateApiClient();
        var login = await userClient.PostAsJsonAsync(
            "/api/auth/login",
            TestRequests.Login("bob"));
        login.EnsureSuccessStatusCode();

        var response = await client.DeleteAsync(
            $"{AdminUsersPath}/{created.UserId}");
        var envelope = await AuthTestFactory.ReadResponseAsync<bool>(response);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("USER_HAS_RELATED_DATA", envelope.Code);

        var exists = await factory.ExecuteDbContextAsync(
            async dbContext => await dbContext.Set<SysUser>()
                .AnyAsync(user => user.UserId == created.UserId));
        Assert.True(exists);
    }

    [Fact]
    public async Task Delete_UnknownUser_Returns404()
    {
        using var factory = new AuthTestFactory();
        using var client = await CreateAuthenticatedClientAsync(factory);

        var response = await client.DeleteAsync($"{AdminUsersPath}/999999");
        var envelope = await AuthTestFactory.ReadResponseAsync<bool>(response);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("USER_NOT_FOUND", envelope.Code);
    }

    [Fact]
    public async Task Delete_CurrentAdminSelf_Returns409()
    {
        using var factory = new AuthTestFactory();
        using var client = await CreateAuthenticatedClientAsync(factory);
        var adminUserId = await factory.ExecuteDbContextAsync(
            async dbContext => await dbContext.Set<SysUser>()
                .Where(user => user.UserName == "alice")
                .Select(user => user.UserId)
                .SingleAsync());

        var response = await client.DeleteAsync(
            $"{AdminUsersPath}/{adminUserId}");
        var envelope = await AuthTestFactory.ReadResponseAsync<bool>(response);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("USER_CANNOT_DELETE_SELF", envelope.Code);
    }

    // ---------- helpers ----------

    private static async Task<HttpClient> CreateAuthenticatedClientAsync(
        AuthTestFactory factory,
        bool grantAdminRole = true)
    {
        await factory.ResetDatabaseAsync();
        await factory.SeedRoleAsync();
        var client = factory.CreateApiClient();

        var registration = await client.PostAsJsonAsync(
            "/api/auth/register",
            TestRequests.ValidRegistration());
        registration.EnsureSuccessStatusCode();

        if (grantAdminRole)
        {
            var adminRole = await factory.SeedRoleAsync("Admin");
            await factory.ExecuteDbContextAsync(async dbContext =>
            {
                var userId = await dbContext.Set<SysUser>()
                    .Where(user => user.UserName == "alice")
                    .Select(user => user.UserId)
                    .SingleAsync();
                dbContext.Add(new UserRole
                {
                    UserId = userId,
                    RoleId = adminRole.RoleId,
                });
                await dbContext.SaveChangesAsync();
                return true;
            });
        }

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

    private static async Task<AdminUserResponse> CreateUserAsync(
        HttpClient client,
        string userName,
        string phone)
    {
        var response = await client.PostAsJsonAsync(
            AdminUsersPath,
            new
            {
                userName,
                password = "Secure123",
                phone,
                nickname = userName,
                email = $"{userName}@example.com",
            });
        response.EnsureSuccessStatusCode();
        var envelope = await AuthTestFactory.ReadResponseAsync<AdminUserResponse>(response);
        return envelope.Data!;
    }
}
