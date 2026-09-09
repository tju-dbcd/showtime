using Microsoft.AspNetCore.Mvc;
using ShowtimeBackend.Common;
using ShowtimeBackend.DTOs.MarketingContent;
using ShowtimeBackend.Services.MarketingContent;

namespace ShowtimeBackend.Controllers.MarketingContent.Client;

[ApiController]
[Route("api/client/shows/{showId:long}/marketing-contents")]
public class ClientMarketingContentController : ControllerBase
{
    private readonly IClientMarketingContentService _clientMarketingService;

    public ClientMarketingContentController(IClientMarketingContentService clientMarketingService)
    {
        _clientMarketingService = clientMarketingService;
    }

    /// <summary>
    /// C端：获取演出关联的生效营销内容列表（支持按类型过滤）
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<IEnumerable<MarketingContentDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<IEnumerable<MarketingContentDto>>>> GetMarketingContents(
        [FromRoute] long showId,
        [FromQuery] MarketingContentType? contentType,
        CancellationToken cancellationToken)
    {
        var contents = await _clientMarketingService.GetClientContentsByShowIdAsync(showId, contentType, cancellationToken);
        return Ok(ApiResponse<IEnumerable<MarketingContentDto>>.Ok(contents, "获取营销内容成功"));
    }
}
