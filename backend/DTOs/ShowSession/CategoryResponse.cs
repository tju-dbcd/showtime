namespace ShowtimeBackend.DTOs.Show;

/// <summary>
/// 演出分类信息传输对象（仅暴露启用状态的分类）
/// </summary>
public record CategoryResponse(
    long CategoryId,
    string CategoryName,
    long? ParentId,
    int SortOrder
);
