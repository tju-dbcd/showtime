using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using ShowtimeBackend.Common;
using ShowtimeBackend.DTOs.Show;
using ShowtimeBackend.Entities.ShowSession;

namespace ShowtimeBackend.Tests.ShowSessionTests;

public sealed class CategoriesControllerTests
{
    [Fact]
    public async Task GetCategories_ReturnsOnlyEnabledCategories_InSortOrder()
    {
        using var factory = new AuthTestFactory();
        await factory.ResetDatabaseAsync();
        await factory.ExecuteDbContextAsync(async dbContext =>
        {
            dbContext.Set<Category>().AddRange(
                new Category { CategoryId = 3, CategoryName = "演唱会", SortOrder = 3, Status = 1, CreateBy = "tests", UpdateBy = "tests" },
                new Category { CategoryId = 1, CategoryName = "话剧", SortOrder = 1, Status = 1, CreateBy = "tests", UpdateBy = "tests" },
                new Category { CategoryId = 2, CategoryName = "音乐剧", SortOrder = 2, Status = 1, CreateBy = "tests", UpdateBy = "tests" },
                new Category { CategoryId = 4, CategoryName = "已禁用分类", SortOrder = 4, Status = 1, CreateBy = "tests", UpdateBy = "tests" });
            await dbContext.SaveChangesAsync();
            // Status 配置了 HasDefaultValue(1)，显式赋 CLR 默认值 0 会被 EF 跳过，
            // 需通过更新方式写入禁用状态
            var disabled = await dbContext.Set<Category>().SingleAsync(c => c.CategoryId == 4);
            disabled.Status = 0;
            await dbContext.SaveChangesAsync();
            return true;
        });
        using var client = factory.CreateApiClient();

        var response = await client.GetAsync("/api/categories");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var apiResponse = await AuthTestFactory.ReadResponseAsync<IEnumerable<CategoryResponse>>(response);
        Assert.True(apiResponse.Success);
        var categories = apiResponse.Data!.ToList();
        Assert.Equal(3, categories.Count);
        Assert.Equal(["话剧", "音乐剧", "演唱会"], categories.Select(c => c.CategoryName));
        Assert.All(categories, c => Assert.NotEqual(4, c.CategoryId));
    }

    [Fact]
    public async Task GetCategories_IsPublic_NoTokenRequired()
    {
        using var factory = new AuthTestFactory();
        await factory.ResetDatabaseAsync();
        await factory.ExecuteDbContextAsync(async dbContext =>
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
            await dbContext.SaveChangesAsync();
            return true;
        });
        using var client = factory.CreateApiClient();

        var response = await client.GetAsync("/api/categories");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var apiResponse = await AuthTestFactory.ReadResponseAsync<IEnumerable<CategoryResponse>>(response);
        Assert.True(apiResponse.Success);
        Assert.Single(apiResponse.Data!);
    }
}
