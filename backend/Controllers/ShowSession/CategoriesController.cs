using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShowtimeBackend.Common;
using ShowtimeBackend.DTOs.Show;
using ShowtimeBackend.Services.ShowSession;

namespace ShowtimeBackend.Controllers.ShowSession;

/// <summary>
/// 演出分类只读接口（前端下拉框动态加载分类，避免硬编码分类 ID）
/// </summary>
[ApiController]
[Route("api/categories")]
[Produces("application/json")]
[AllowAnonymous]
public class CategoriesController : ControllerBase
{
    private readonly ICategoryService _categoryService;

    public CategoriesController(ICategoryService categoryService)
    {
        _categoryService = categoryService;
    }

    /// <summary>
    /// 获取所有启用的演出分类
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<IEnumerable<CategoryResponse>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<IEnumerable<CategoryResponse>>>> GetAll(
        CancellationToken cancellationToken)
    {
        var categories = await _categoryService.GetEnabledCategoriesAsync(cancellationToken);
        return Ok(ApiResponse<IEnumerable<CategoryResponse>>.Ok(categories, "获取分类列表成功"));
    }
}
