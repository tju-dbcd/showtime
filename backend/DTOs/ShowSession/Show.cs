namespace ShowtimeBackend.DTOs.Show;

/// <summary>
/// 创建演出请求参数
/// </summary>
public record CreateShowRequest(
    string ShowName,
    long CategoryId,
    string? Description,
    int? DurationMinutes,
    string? PosterUrl
);

/// <summary>
/// 更新演出请求参数
/// </summary>
public record UpdateShowRequest(
    string ShowName,
    long CategoryId,
    string? Description,
    int? DurationMinutes,
    string? PosterUrl,
    string Status // "DRAFT", "PUBLISHED", "UNPUBLISHED"
);

/// <summary>
/// 演出列表查询参数
/// </summary>
public record ShowQueryRequest(
    int PageIndex = 1,
    int PageSize = 10,
    string? Keyword = null,
    long? CategoryId = null,
    string? Status = null
);

/// <summary>
/// 演出信息传输对象
/// </summary>
public record ShowDto(
    long ShowId,
    string ShowName,
    long CategoryId,
    string? Description,
    int? DurationMinutes,
    string? PosterUrl,
    string Status,
    string AuditStatus,
    DateTime CreateTime
);

public record PagedShowResponse(
    IEnumerable<ShowDto> Items,
    long TotalCount,
    int PageIndex,
    int PageSize
);
