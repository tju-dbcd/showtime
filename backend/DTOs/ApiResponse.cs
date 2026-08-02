namespace ShowtimeBackend.DTOs;

/// <summary>
/// 单资源接口的统一成功响应外层，避免直接暴露实体对象。
/// </summary>
public sealed record ApiResponse<T>(T Data);

/// <summary>
/// 列表接口的分页数据；页码从 1 开始。
/// </summary>
public sealed record PagedResponse<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);
