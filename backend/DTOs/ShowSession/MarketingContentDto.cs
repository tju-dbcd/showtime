using System.ComponentModel.DataAnnotations;

namespace ShowtimeBackend.DTOs.MarketingContent;

public enum MarketingContentType
{
    NOTICE,
    AD,
    PROMOTION
}

public enum MarketingContentStatus
{
    ENABLED,
    DISABLED
}

public record CreateMarketingContentRequest(
    [Required(ErrorMessage = "演出 ID 不能为空")]
    long ShowId,

    [Required(ErrorMessage = "内容类型不能为空")]
    MarketingContentType ContentType,

    [Required(ErrorMessage = "标题不能为空")]
    [StringLength(200, ErrorMessage = "标题不能超过 200 个字符")]
    string Title,

    string? ContentText,

    [StringLength(500, ErrorMessage = "图片 URL 不能超过 500 个字符")]
    [RegularExpression(@"^(https?://\S+|/\S*)$", ErrorMessage = "图片 URL 必须是 http(s) 绝对地址或以 / 开头的相对路径，且不能包含空白字符")]
    string? ImageUrl,

    int SortOrder = 0,

    MarketingContentStatus Status = MarketingContentStatus.ENABLED,

    DateTime? PublishTime = null
);

public record UpdateMarketingContentRequest(
    [Required(ErrorMessage = "内容类型不能为空")]
    MarketingContentType ContentType,

    [Required(ErrorMessage = "标题不能为空")]
    [StringLength(200, ErrorMessage = "标题不能超过 200 个字符")]
    string Title,

    string? ContentText,

    [StringLength(500, ErrorMessage = "图片 URL 不能超过 500 个字符")]
    [RegularExpression(@"^(https?://\S+|/\S*)$", ErrorMessage = "图片 URL 必须是 http(s) 绝对地址或以 / 开头的相对路径，且不能包含空白字符")]
    string? ImageUrl,

    int SortOrder,

    MarketingContentStatus Status,

    DateTime? PublishTime
);

public record MarketingContentQueryRequest(
    long? ShowId = null,
    MarketingContentType? ContentType = null,
    MarketingContentStatus? Status = null,
    string? Keyword = null,
    int PageIndex = 1,
    int PageSize = 10
);

public record MarketingContentDto(
    long ContentId,
    long ShowId,
    MarketingContentType ContentType,
    string Title,
    string? ContentText,
    string? ImageUrl,
    int SortOrder,
    MarketingContentStatus Status,
    DateTime? PublishTime,
    DateTime CreateTime
);
