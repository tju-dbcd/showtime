using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using ShowtimeBackend.Common;
using ShowtimeBackend.DTOs.UserPermission;
using ShowtimeBackend.Data;
using ShowtimeBackend.Entities.SeatZone;
using ShowtimeBackend.Entities.ShowSession;
using ShowtimeBackend.Entities.UserPermission;

namespace ShowtimeBackend.Tests.ShowSessionTests;

/// <summary>
/// 验证枚举字段进入 DTO 后，非法取值在 JSON 模型绑定阶段即被拒绝（400），
/// 合法取值可正常处理——这是枚举进入 OpenAPI schema 后的契约保障。
/// </summary>
public sealed class EnumBindingTests
{
    [Fact]
    public async Task UpdateSessionStatus_InvalidEnumValue_ReturnsBadRequest()
    {
        using var context = await AdminTestContext.CreateAsync();
        using var client = context.Client;

        var response = await client.PutAsJsonAsync(
            "/api/admin/sessions/1/status",
            new { status = "UNKNOWN_STATUS" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var apiResponse = await AuthTestFactory.ReadResponseAsync<object>(response);
        Assert.False(apiResponse.Success);
        Assert.Equal("VALIDATION_FAILED", apiResponse.Code);
    }

    [Fact]
    public async Task UpdateSessionStatus_ValidEnumValue_ReturnsOk()
    {
        using var context = await AdminTestContext.CreateAsync();
        await context.Factory.ExecuteDbContextAsync(
            dbContext => SeedSessionAsync(dbContext, SessionStatus.UPCOMING));
        using var client = context.Client;

        var response = await client.PutAsJsonAsync(
            "/api/admin/sessions/1/status",
            new { status = "ONSALE" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var apiResponse = await AuthTestFactory.ReadResponseAsync<object>(response);
        Assert.True(apiResponse.Success);
        var dbStatus = await context.Factory.ExecuteDbContextAsync(
            dbContext => dbContext.Set<ShowSession>().Where(s => s.SessionId == 1).Select(s => s.SessionStatus).SingleAsync());
        Assert.Equal("ONSALE", dbStatus);
    }

    [Fact]
    public async Task ConfigurePriceStrategies_InvalidPriceType_ReturnsBadRequest()
    {
        using var context = await AdminTestContext.CreateAsync();
        using var client = context.Client;

        var response = await client.PostAsJsonAsync(
            "/api/admin/sessions/1/pricing-strategies",
            new[]
            {
                new { seatSectionId = 1, priceType = "normal", price = 100m }
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var apiResponse = await AuthTestFactory.ReadResponseAsync<object>(response);
        Assert.Equal("VALIDATION_FAILED", apiResponse.Code);
    }

    [Fact]
    public async Task ConfigurePriceStrategies_ValidEnumValue_ReturnsOk()
    {
        using var context = await AdminTestContext.CreateAsync();
        await context.Factory.ExecuteDbContextAsync(
            dbContext => SeedSessionAsync(dbContext, SessionStatus.PRESALE));
        using var client = context.Client;

        var response = await client.PostAsJsonAsync(
            "/api/admin/sessions/1/pricing-strategies",
            new[]
            {
                new { seatSectionId = 1, priceType = "STANDARD", price = 100m, priority = 0 }
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var apiResponse = await AuthTestFactory.ReadResponseAsync<object>(response);
        Assert.True(apiResponse.Success);
    }

    [Fact]
    public async Task UpdateShow_InvalidStatus_ReturnsBadRequest()
    {
        using var context = await AdminTestContext.CreateAsync();
        using var client = context.Client;

        var response = await client.PutAsJsonAsync(
            "/api/admin/shows/1",
            new { showName = "新名字", categoryId = 1, status = "INVALID" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var apiResponse = await AuthTestFactory.ReadResponseAsync<object>(response);
        Assert.Equal("VALIDATION_FAILED", apiResponse.Code);
    }

    /// <summary>植入场次及外键链（分类→演出→场馆→座位图→场次），满足 SQLite 外键约束。</summary>
    private static Task<bool> SeedSessionAsync(AppDbContext dbContext, SessionStatus status)
    {
        dbContext.Set<Category>().Add(new Category
        {
            CategoryId = 1,
            CategoryName = "话剧",
            SortOrder = 1,
            Status = 1,
            CreateBy = "tests",
            UpdateBy = "tests"
        });
        dbContext.Set<Venue>().Add(new Venue
        {
            VenueId = 1000,
            VenueName = "测试场馆",
            Status = "ENABLED",
            CreateBy = "tests",
            UpdateBy = "tests"
        });
        dbContext.Set<SeatMap>().Add(new SeatMap
        {
            SeatMapId = 100,
            VenueId = 1000,
            MapCode = "MAP-100",
            MapName = "测试座位图",
            MapVersion = "V1",
            IsDefault = true,
            MapStatus = "ENABLED",
            CreateBy = "tests",
            UpdateBy = "tests"
        });
        dbContext.Set<Show>().Add(new Show
        {
            ShowId = 10,
            CategoryId = 1,
            ShowName = "测试演出",
            Status = ShowStatus.DRAFT.ToDbString(),
            AuditStatus = ShowAuditStatus.PENDING.ToDbString(),
            CreateBy = "tests",
            UpdateBy = "tests"
        });
        dbContext.Set<ShowSession>().Add(new ShowSession
        {
            SessionId = 1,
            ShowId = 10,
            SeatMapId = 100,
            StartTime = DateTime.UtcNow.AddDays(1),
            EndTime = DateTime.UtcNow.AddDays(1).AddHours(2),
            SaleStartTime = DateTime.UtcNow.AddHours(-1),
            SaleEndTime = DateTime.UtcNow.AddHours(1),
            SessionStatus = status.ToDbString(),
            CreateBy = "tests",
            UpdateBy = "tests"
        });
        return dbContext.SaveChangesAsync().ContinueWith(t => t.Result > 0);
    }

    /// <summary>承载已登录管理员客户端与测试工厂的可释放上下文。</summary>
    private sealed class AdminTestContext : IDisposable
    {
        public AuthTestFactory Factory { get; private init; } = null!;

        public HttpClient Client { get; private init; } = null!;

        public static async Task<AdminTestContext> CreateAsync()
        {
            var factory = new AuthTestFactory();
            try
            {
                await factory.ResetDatabaseAsync();
                await factory.SeedRoleAsync(); // USER
                var adminRole = await factory.SeedRoleAsync("Admin");
                using (var client = factory.CreateApiClient())
                {
                    var registration = await client.PostAsJsonAsync(
                        "/api/auth/register",
                        TestRequests.ValidRegistration());
                    registration.EnsureSuccessStatusCode();
                }
                await factory.ExecuteDbContextAsync(async dbContext =>
                {
                    var userId = await dbContext.Set<SysUser>().Select(u => u.UserId).SingleAsync();
                    dbContext.Add(new UserRole { UserId = userId, RoleId = adminRole.RoleId });
                    await dbContext.SaveChangesAsync();
                    return true;
                });
                using (var loginClient = factory.CreateApiClient())
                {
                    var login = await loginClient.PostAsJsonAsync(
                        "/api/auth/login",
                        TestRequests.Login("alice"));
                    login.EnsureSuccessStatusCode();
                    var loginResponse = await AuthTestFactory.ReadResponseAsync<LoginResponse>(login);
                    var client = factory.CreateApiClient();
                    client.DefaultRequestHeaders.Authorization =
                        new AuthenticationHeaderValue("Bearer", loginResponse.Data!.AccessToken);
                    return new AdminTestContext { Factory = factory, Client = client };
                }
            }
            catch
            {
                factory.Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            Client.Dispose();
            Factory.Dispose();
        }
    }
}
