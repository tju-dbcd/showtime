namespace ShowtimeBackend.Common;

/// <summary>
/// 列表接口的分页数据；页码从 1 开始。
/// </summary>
public sealed record PagedResponse<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);
