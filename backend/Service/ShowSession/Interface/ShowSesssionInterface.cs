using ShowtimeBackend.Dtos.Client;

namespace ShowtimeBackend.Services.Interfaces;

public interface IShowSessionService
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
