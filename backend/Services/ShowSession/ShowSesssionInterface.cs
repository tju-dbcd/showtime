using ShowtimeBackend.Dtos.Client;
using ShowtimeBackend.Dtos.Admin;

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
    /// 手动变更场次状态（如紧急停售/下架）
    /// </summary>
    Task<bool> UpdateSessionStatusAsync(long sessionId, string newStatus, CancellationToken cancellationToken = default);
}
