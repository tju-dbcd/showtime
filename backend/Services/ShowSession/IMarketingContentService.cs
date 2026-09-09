using ShowtimeBackend.Common;
using ShowtimeBackend.DTOs.MarketingContent;

namespace ShowtimeBackend.Services.MarketingContent;

//整合两端的接口
public interface IAdminMarketingContentService
{
    Task<MarketingContentDto> CreateContentAsync(
        CreateMarketingContentRequest request,
        string operatorName = "admin",
        CancellationToken cancellationToken = default);

    Task<bool> UpdateContentAsync(
        long contentId,
        UpdateMarketingContentRequest request,
        string operatorName = "admin",
        CancellationToken cancellationToken = default);

    Task<bool> DeleteContentAsync(long contentId, CancellationToken cancellationToken = default);

    Task<MarketingContentDto> GetContentByIdAsync(long contentId, CancellationToken cancellationToken = default);

    Task<PagedResponse<MarketingContentDto>> GetContentsAsync(
        MarketingContentQueryRequest query,
        CancellationToken cancellationToken = default);
}

public interface IClientMarketingContentService
{
    Task<IEnumerable<MarketingContentDto>> GetClientContentsByShowIdAsync(
        long showId,
        MarketingContentType? contentType = null,
        CancellationToken cancellationToken = default);
}
