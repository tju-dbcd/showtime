using ShowtimeBackend.DTOs.Show;

namespace ShowtimeBackend.Services.ShowSession;

public interface ICategoryService
{
    /// <summary>
    /// 获取所有启用的演出分类（按排序号升序）
    /// </summary>
    Task<IEnumerable<CategoryResponse>> GetEnabledCategoriesAsync(CancellationToken cancellationToken = default);
}
