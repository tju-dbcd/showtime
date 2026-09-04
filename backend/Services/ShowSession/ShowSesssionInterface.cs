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
    Task<IEnumerable<ShowSessionDto>> GetOnSaleSessionsAsync(long showId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取指定场次的所有区域票价策略
    /// </summary>
    Task<IEnumerable<PricingStrategyDto>> GetPricingStrategiesAsync(long sessionId, CancellationToken cancellationToken = default);
}

public interface IClientShowService
{
    Task<PagedResponse<ShowDto>> GetClientShowsAsync(ShowQueryRequest query, CancellationToken cancellationToken = default);
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
    Task ConfigurePriceStrategiesAsync(long sessionId, IEnumerable<CreatePriceStrategyRequest> requests, string operatorName = "admin", CancellationToken cancellationToken = default);

    /// <summary>
    /// 为场次配置动态调价规则
    /// </summary>
    Task ConfigureDynamicPricingRulesAsync(long sessionId, IEnumerable<CreateDynamicPricingRuleRequest> requests, string operatorName = "admin", CancellationToken cancellationToken = default);

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
