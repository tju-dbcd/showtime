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
    /// 获取指定场次各区域的票价展示口径（实时报价）。
    /// </summary>
    /// <remarks>
    /// 返回价为 <b>实时展示报价</b>：以“当前时间”为 evaluationTime 计算动态调价结果，仅用于前端列表/详情展示与比价。<para/>
    /// 注意：<b>最终成交价不取自本端点</b>。下单/改签结算链路以“座位锁创建时刻（seatLock.CreateTime）”
    /// 为 evaluationTime 重新计价，成交价以锁定时点锁定。因此展示价与成交价允许不一致
    /// （下单期间临近开演的动态调价不影响已锁定时点订单），属预期行为。
    /// </remarks>
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
