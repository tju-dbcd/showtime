using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShowtimeBackend.Common;
using ShowtimeBackend.DTOs.MarketingContent;
using ShowtimeBackend.Services.MarketingContent;

namespace ShowtimeBackend.Controllers.MarketingContent.Admin;

[ApiController]
[Route("api/admin/marketing-contents")]
[Authorize(Roles = "Admin")]
public class AdminMarketingContentController : ControllerBase
{
    private readonly IAdminMarketingContentService _marketingService;

    public AdminMarketingContentController(IAdminMarketingContentService marketingService)
    {
        _marketingService = marketingService;
    }

    /// <summary>
    /// 创建营销内容（含图文）
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<MarketingContentDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse<MarketingContentDto>), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<MarketingContentDto>>> CreateContent(
        [FromBody] CreateMarketingContentRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var operatorName = User.Identity?.Name ?? "admin";
            var created = await _marketingService.CreateContentAsync(request, operatorName, cancellationToken);
            return Created($"/api/admin/marketing-contents/{created.ContentId}", ApiResponse<MarketingContentDto>.Ok(created, "营销内容创建成功"));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ApiResponse<MarketingContentDto>.Fail("INVALID_ARGUMENT", ex.Message));
        }
    }

    /// <summary>
    /// 更新营销内容
    /// </summary>
    [HttpPut("{contentId:long}")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<object>>> UpdateContent(
        [FromRoute] long contentId,
        [FromBody] UpdateMarketingContentRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var operatorName = User.Identity?.Name ?? "admin";
            await _marketingService.UpdateContentAsync(contentId, request, operatorName, cancellationToken);
            return Ok(ApiResponse<object>.Ok(null!, "营销内容更新成功"));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiResponse<object>.Fail("NOT_FOUND", ex.Message));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ApiResponse<object>.Fail("INVALID_ARGUMENT", ex.Message));
        }
    }

    /// <summary>
    /// 删除营销内容
    /// </summary>
    [HttpDelete("{contentId:long}")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<object>>> DeleteContent(
        [FromRoute] long contentId,
        CancellationToken cancellationToken)
    {
        try
        {
            await _marketingService.DeleteContentAsync(contentId, cancellationToken);
            return Ok(ApiResponse<object>.Ok(null!, "营销内容删除成功"));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiResponse<object>.Fail("NOT_FOUND", ex.Message));
        }
    }

    /// <summary>
    /// 获取营销内容详情
    /// </summary>
    [HttpGet("{contentId:long}")]
    [ProducesResponseType(typeof(ApiResponse<MarketingContentDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<MarketingContentDto>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<MarketingContentDto>>> GetContentById(
        [FromRoute] long contentId,
        CancellationToken cancellationToken)
    {
        try
        {
            var content = await _marketingService.GetContentByIdAsync(contentId, cancellationToken);
            return Ok(ApiResponse<MarketingContentDto>.Ok(content, "获取营销内容详情成功"));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiResponse<MarketingContentDto>.Fail("NOT_FOUND", ex.Message));
        }
    }

    /// <summary>
    /// 分页查询营销内容列表
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PagedResponse<MarketingContentDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<PagedResponse<MarketingContentDto>>>> GetContents(
        [FromQuery] MarketingContentQueryRequest query,
        CancellationToken cancellationToken)
    {
        var result = await _marketingService.GetContentsAsync(query, cancellationToken);
        return Ok(ApiResponse<PagedResponse<MarketingContentDto>>.Ok(result, "获取营销内容列表成功"));
    }
}
