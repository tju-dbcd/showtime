using ShowtimeBackend.Common;
using ShowtimeBackend.DTOs.ShowSessionChange;
using ShowtimeBackend.DTOs.Show;
using ShowtimeBackend.DTOs.ShowSessionDto;

namespace ShowtimeBackend.Services.ShowSession;

public interface IClientShowSessionService
{
    /// <summary>
    /// 获取指定演出下所有有效可售的场次列表
    /// </summary>
    /// <param name="showId">演出ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task<IEnumerable<ShowSessionDto>> GetOnSaleSessionsAsync(long showId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取指定场次的所有区域票价策略
    /// </summary>
    /// <param name="sessionId">场次ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task<IEnumerable<PricingStrategyDto>> GetPricingStrategiesAsync(long sessionId, CancellationToken cancellationToken = default);
}

public interface IClientShowService
{
    /// <summary>
    /// 获取 C 端已上架且审核通过的演出
    /// </summary>
    Task<PagedResponse<ShowDto>> GetClientShowsAsync(ShowQueryRequest query, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取已上架演出的详情
    /// </summary>
    Task<ShowDto> GetClientShowByIdAsync(long showId, CancellationToken cancellationToken = default);
}

public interface IAdminShowSessionService
{
    /// <summary>
    /// 为指定演出排布/创建新场次
    /// </summary>
    Task<ShowSessionDto> CreateSessionAsync(long showId, CreateShowSessionRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// 为场次批量配置区域票价策略
    /// </summary>
    Task<bool> ConfigurePriceStrategiesAsync(long sessionId, IEnumerable<CreatePriceStrategyRequest> requests, CancellationToken cancellationToken = default);

    /// <summary>
    /// 手动变更场次状态
    /// </summary>
    Task<bool> UpdateSessionStatusAsync(long sessionId, SessionStatus newStatus, CancellationToken cancellationToken = default);

    Task<IEnumerable<ShowSessionDto>> GetAdminSessionsByShowIdAsync(long showId, CancellationToken cancellationToken = default);
}

public interface IAdminShowService
{
    Task<ShowDto> CreateShowAsync(CreateShowRequest request, CancellationToken cancellationToken = default);
    Task<bool> UpdateShowAsync(long showId, UpdateShowRequest request, CancellationToken cancellationToken = default);
    Task<bool> DeleteShowAsync(long showId, CancellationToken cancellationToken = default);
    Task<ShowDto> GetShowByIdAsync(long showId, CancellationToken cancellationToken = default);
    Task<PagedResponse<ShowDto>> GetShowsAsync(ShowQueryRequest query, CancellationToken cancellationToken = default);
}
