import { client } from './request';
import type { components } from './types';

// ========== 枚举类型 ==========
export type PriceType = components['schemas']['PriceType'];
export type SessionStatus = components['schemas']['SessionStatus'];
export type OrderStatus = components['schemas']['OrderStatus'];
export type ShowStatus = components['schemas']['ShowStatus'];
export type OrderItemStatus = components['schemas']['OrderItemStatus'];

// ========== 分类相关 ==========
export type CategoryResponse = components['schemas']['CategoryResponse'];

// 获取分类列表（公开接口）
export const getCategories = () => {
  return client.GET('/api/categories', {});
};

// ========== 演出相关 ==========
export type CreateShowRequest = components['schemas']['CreateShowRequest'];
export type UpdateShowRequest = components['schemas']['UpdateShowRequest'];
export type ShowDto = components['schemas']['ShowDto'];

// 创建演出
export const createShow = (data: CreateShowRequest) => {
  return client.POST('/api/admin/shows', {
    body: data,
  });
};

// 获取演出列表（管理端）
export const getShowList = (params?: {
  PageIndex?: number;
  PageSize?: number;
  Keyword?: string;
  CategoryId?: number;
  Status?: ShowStatus;
}) => {
  return client.GET('/api/admin/shows', {
    params: { query: params },
  });
};

// 获取演出详情
export const getShowDetail = (showId: number) => {
  return client.GET('/api/admin/shows/{showId}', {
    params: { path: { showId } },
  });
};

// ========== 演出相关 ==========
export type UpdateShowAuditStatusRequest = components['schemas']['UpdateShowAuditStatusRequest'];

export const updateShowAuditStatus = (showId: number, data: UpdateShowAuditStatusRequest) => {
  return client.PUT('/api/admin/shows/{showId}/audit-status', {
    params: { path: { showId } },
    body: data,
  });
};

export const updateShow = (showId: number, data: UpdateShowRequest) => {
  return client.PUT('/api/admin/shows/{showId}', {
    params: { path: { showId } },
    body: data,
  });
};

// 删除演出
export const deleteShow = (showId: number) => {
  return client.DELETE('/api/admin/shows/{showId}', {
    params: { path: { showId } },
  });
};

// ========== 场次相关 ==========
export type CreateShowSessionRequest = components['schemas']['CreateShowSessionRequest'];
export type CreatePriceStrategyRequest = components['schemas']['CreatePriceStrategyRequest'];
export type UpdateSessionStatusRequest = components['schemas']['UpdateSessionStatusRequest'];
export type ShowSessionDto = components['schemas']['ShowSessionDto'];

// 获取某演出下的场次列表
export const getShowSessions = (showId: number) => {
  return client.GET('/api/admin/shows/{showId}/sessions', {
    params: { path: { showId } },
  });
};

// 给演出添加场次
export const addSession = (showId: number, data: CreateShowSessionRequest) => {
  return client.POST('/api/admin/shows/{showId}/sessions', {
    params: { path: { showId } },
    body: data,
  });
};

// 编辑场次基础排期信息
export type UpdateShowSessionRequest = components['schemas']['UpdateShowSessionRequest'];

export const updateSession = (sessionId: number, data: UpdateShowSessionRequest) => {
  return client.PUT('/api/admin/sessions/{sessionId}', {
    params: { path: { sessionId } },
    body: data,
  });
};

// 给场次添加定价策略（数组）
export const addPricingStrategies = (sessionId: number, data: CreatePriceStrategyRequest[]) => {
  return client.POST('/api/admin/sessions/{sessionId}/pricing-strategies', {
    params: { path: { sessionId } },
    body: data,
  });
};

// 管理端：获取场次全部基础票价策略（含禁用档与售票窗口，供维护弹窗使用）
export type AdminPriceStrategyDto = components['schemas']['AdminPriceStrategyDto'];

export const getAdminPricingStrategies = (sessionId: number) => {
  return client.GET('/api/admin/sessions/{sessionId}/pricing-strategies', {
    params: { path: { sessionId } },
  });
};

// 更新场次状态
export const updateSessionStatus = (sessionId: number, data: UpdateSessionStatusRequest) => {
  return client.PUT('/api/admin/sessions/{sessionId}/status', {
    params: { path: { sessionId } },
    body: data,
  });
};

// ========== 场馆相关（管理端） ==========
export type VenueResponse = components['schemas']['VenueResponse'];

// 获取启用中的场馆列表（新建座位图时选择场馆）
export const getVenues = () => {
  return client.GET('/api/admin/venues', {});
};

// ========== 座位图/票区相关 ==========
export type SeatMapResponse = components['schemas']['SeatMapResponse'];
export type SeatMapRequest = components['schemas']['SeatMapRequest'];
export type SeatSectionResponse = components['schemas']['SeatSectionResponse'];
export type SeatSectionRequest = components['schemas']['SeatSectionRequest'];
export type SeatResponse = components['schemas']['SeatResponse'];
export type SeatRequest = components['schemas']['SeatRequest'];
export type SeatBatchUpdateRequest = components['schemas']['SeatBatchUpdateRequest'];
export type SeatBatchUpdateResponse = components['schemas']['SeatBatchUpdateResponse'];

// 新建座位图
export const createSeatMap = (data: SeatMapRequest) => {
  return client.POST('/api/admin/seat-maps', { body: data });
};

// 获取座位图列表
export const getSeatMapList = (params?: {
  VenueId?: number;
  MapStatus?: string;
  Keyword?: string;
  Page?: number;
  PageSize?: number;
}) => {
  return client.GET('/api/admin/seat-maps', {
    params: { query: params },
  });
};

// 新建票区
export const createSeatSection = (seatMapId: number, data: SeatSectionRequest) => {
  return client.POST('/api/admin/seat-maps/{seatMapId}/sections', {
    params: { path: { seatMapId } },
    body: data,
  });
};

// 获取某座位图下的票区列表
export const getSeatSections = (seatMapId: number, params?: {
  SectionType?: string;
  IsSellable?: boolean;
  Page?: number;
  PageSize?: number;
}) => {
  return client.GET('/api/admin/seat-maps/{seatMapId}/sections', {
    params: { path: { seatMapId }, query: params },
  });
};

// 获取某票区下的座位列表
export const getSeats = (seatSectionId: number, params?: {
  SeatType?: string;
  SeatStatus?: string;
  IsSellable?: boolean;
  RowCode?: string;
  Page?: number;
  PageSize?: number;
}) => {
  return client.GET('/api/admin/seat-sections/{seatSectionId}/seats', {
    params: { path: { seatSectionId }, query: params },
  });
};

// 单个创建座位
export const createSeat = (seatSectionId: number, data: SeatRequest) => {
  return client.POST('/api/admin/seat-sections/{seatSectionId}/seats', {
    params: { path: { seatSectionId } },
    body: data,
  });
};

// 批量编辑座位（PATCH）
export const batchUpdateSeats = (seatSectionId: number, data: SeatBatchUpdateRequest) => {
  return client.PATCH('/api/admin/seat-sections/{seatSectionId}/seats', {
    params: { path: { seatSectionId } },
    body: data,
  });
};

// 更新单个座位
export const updateSeat = (seatId: number, data: SeatRequest) => {
  return client.PUT('/api/admin/seats/{seatId}', {
    params: { path: { seatId } },
    body: data,
  });
};

// 删除单个座位
export const deleteSeat = (seatId: number) => {
  return client.DELETE('/api/admin/seats/{seatId}', {
    params: { path: { seatId } },
  });
};

// ========== 订单相关（管理端） ==========
export type AdminOrderSummary = components['schemas']['AdminOrderSummaryResponse'];
export type OrderDetail = components['schemas']['OrderResponse'];
export type OrderItem = components['schemas']['OrderItemResponse'];
export type Payment = components['schemas']['PaymentResponse'];
export type TicketIssuance = components['schemas']['TicketIssuanceResponse'];

// 管理端：分页查询全部订单
export const getAdminOrderList = (params?: {
  Status?: OrderStatus;
  Keyword?: string;
  Page?: number;
  PageSize?: number;
}) => {
  return client.GET('/api/admin/orders', {
    params: { query: params },
  });
};

// 管理端：获取任意订单详情
export const getAdminOrderDetail = (orderId: number) => {
  return client.GET('/api/admin/orders/{orderId}', {
    params: { path: { orderId } },
  });
};

// 管理端：取消订单（仅 PENDING_PAY）
export const adminCancelOrder = (orderId: number) => {
  return client.PATCH('/api/admin/orders/{orderId}/cancel', {
    params: { path: { orderId } },
  });
};

// 管理端：为历史 PAID 订单补偿出票，或修复 ISSUED 缺票订单
export const issueOrderTickets = (orderId: number) => {
  return client.POST('/api/admin/orders/{orderId}/issue', {
    params: { path: { orderId } },
  });
};

// ========== 退票审核（管理端） ==========
export type RefundSummary = components['schemas']['RefundSummaryResponse'];
export type RefundDetail = components['schemas']['RefundResponse'];
export type RefundApproveStatus = components['schemas']['RefundApproveStatus'];
export type RefundStatus = components['schemas']['RefundStatus'];
export type RefundType = components['schemas']['RefundType'];

// 管理端：分页查询退票申请
export const getRefundList = (params?: {
  ApproveStatus?: RefundApproveStatus;
  RefundStatus?: RefundStatus;
  OrderId?: number;
  UserId?: number;
  RefundNo?: string;
  Page?: number;
  PageSize?: number;
}) => {
  return client.GET('/api/admin/refunds', {
    params: { query: params },
  });
};

// 管理端：获取退票申请详情
export const getRefundDetail = (refundId: number) => {
  return client.GET('/api/admin/refunds/{refundId}', {
    params: { path: { refundId } },
  });
};

// 管理端：通过退票申请
export const approveRefund = (refundId: number, remark?: string | null) => {
  return client.POST('/api/admin/refunds/{refundId}/approve', {
    params: { path: { refundId } },
    body: { remark: remark ?? null },
  });
};

// 管理端：驳回退票申请
export const rejectRefund = (refundId: number, remark: string) => {
  return client.POST('/api/admin/refunds/{refundId}/reject', {
    params: { path: { refundId } },
    body: { remark },
  });
};

// ========== 改签审核（管理端） ==========
export type ExchangeSummary = components['schemas']['ExchangeSummaryResponse'];
export type ExchangeDetail = components['schemas']['ExchangeResponse'];
export type ExchangeApproveStatus = components['schemas']['ExchangeApproveStatus'];
export type ExchangeStatus = components['schemas']['ExchangeStatus'];

// 管理端：分页查询改签申请
export const getExchangeList = (params?: {
  ApproveStatus?: ExchangeApproveStatus;
  ExchangeStatus?: ExchangeStatus;
  OriginalOrderId?: number;
  UserId?: number;
  ExchangeNo?: string;
  Page?: number;
  PageSize?: number;
}) => {
  return client.GET('/api/admin/exchanges', {
    params: { query: params },
  });
};

// 管理端：获取改签申请详情
export const getExchangeDetail = (exchangeId: number) => {
  return client.GET('/api/admin/exchanges/{exchangeId}', {
    params: { path: { exchangeId } },
  });
};

// 管理端：通过改签申请
export const approveExchange = (exchangeId: number, remark?: string | null) => {
  return client.POST('/api/admin/exchanges/{exchangeId}/approve', {
    params: { path: { exchangeId } },
    body: { remark: remark ?? null },
  });
};

// 管理端：驳回改签申请
export const rejectExchange = (exchangeId: number, remark: string) => {
  return client.POST('/api/admin/exchanges/{exchangeId}/reject', {
    params: { path: { exchangeId } },
    body: { remark },
  });
};

// ========== 退票策略管理（管理端） ==========
export type RefundPolicy = components['schemas']['RefundPolicyResponse'];
export type SaveRefundPolicyRequest = components['schemas']['SaveRefundPolicyRequest'];

// 管理端：分页查询退票策略
export const getRefundPolicyList = (params?: {
  ShowId?: number;
  Status?: number;
  Page?: number;
  PageSize?: number;
}) => {
  return client.GET('/api/admin/refund-policies', {
    params: { query: params },
  });
};

// 管理端：创建退票策略
export const createRefundPolicy = (data: SaveRefundPolicyRequest) => {
  return client.POST('/api/admin/refund-policies', { body: data });
};

// 管理端：更新退票策略
export const updateRefundPolicy = (policyId: number, data: SaveRefundPolicyRequest) => {
  return client.PUT('/api/admin/refund-policies/{policyId}', {
    params: { path: { policyId } },
    body: data,
  });
};

// 管理端：启停退票策略
export const updateRefundPolicyStatus = (policyId: number, data: { status: number }) => {
  return client.PATCH('/api/admin/refund-policies/{policyId}/status', {
    params: { path: { policyId } },
    body: data,
  });
};

// ========== 改签策略管理（管理端） ==========
export type ExchangePolicy = components['schemas']['ExchangePolicyResponse'];
export type SaveExchangePolicyRequest = components['schemas']['SaveExchangePolicyRequest'];

// 管理端：分页查询改签策略
export const getExchangePolicyList = (params?: {
  ShowId?: number;
  Status?: number;
  Page?: number;
  PageSize?: number;
}) => {
  return client.GET('/api/admin/exchange-policies', {
    params: { query: params },
  });
};

// 管理端：创建改签策略
export const createExchangePolicy = (data: SaveExchangePolicyRequest) => {
  return client.POST('/api/admin/exchange-policies', { body: data });
};

// 管理端：更新改签策略
export const updateExchangePolicy = (policyId: number, data: SaveExchangePolicyRequest) => {
  return client.PUT('/api/admin/exchange-policies/{policyId}', {
    params: { path: { policyId } },
    body: data,
  });
};

// 管理端：启停改签策略
export const updateExchangePolicyStatus = (policyId: number, data: { status: number }) => {
  return client.PATCH('/api/admin/exchange-policies/{policyId}/status', {
    params: { path: { policyId } },
    body: data,
  });
};

// ========== 座位规则管理（管理端） ==========
export type SeatRule = components['schemas']['SeatRuleResponse'];
export type SaveSeatRuleRequest = components['schemas']['SeatRuleRequest'];
export type SeatRuleScope = components['schemas']['SeatRuleScopeResponse'];
export type SeatRuleScopeRequest = components['schemas']['SeatRuleScopeRequest'];

// 管理端：分页查询座位规则
export const getSeatRuleList = (params?: {
  RuleType?: string;
  RuleStatus?: string;
  Page?: number;
  PageSize?: number;
}) => {
  return client.GET('/api/admin/seat-rules', {
    params: { query: params },
  });
};

// 管理端：获取座位规则详情
export const getSeatRuleDetail = (seatRuleId: number) => {
  return client.GET('/api/admin/seat-rules/{seatRuleId}', {
    params: { path: { seatRuleId } },
  });
};

// 管理端：创建座位规则
export const createSeatRule = (data: SaveSeatRuleRequest) => {
  return client.POST('/api/admin/seat-rules', { body: data });
};

// 管理端：更新座位规则
export const updateSeatRule = (seatRuleId: number, data: SaveSeatRuleRequest) => {
  return client.PUT('/api/admin/seat-rules/{seatRuleId}', {
    params: { path: { seatRuleId } },
    body: data,
  });
};

// 管理端：删除座位规则
export const deleteSeatRule = (seatRuleId: number) => {
  return client.DELETE('/api/admin/seat-rules/{seatRuleId}', {
    params: { path: { seatRuleId } },
  });
};

// 管理端：获取规则作用域列表
export const getSeatRuleScopes = (seatRuleId: number) => {
  return client.GET('/api/admin/seat-rules/{seatRuleId}/scopes', {
    params: { path: { seatRuleId } },
  });
};

// 管理端：创建规则作用域
export const createSeatRuleScope = (seatRuleId: number, data: SeatRuleScopeRequest) => {
  return client.POST('/api/admin/seat-rules/{seatRuleId}/scopes', {
    params: { path: { seatRuleId } },
    body: data,
  });
};

// 管理端：删除规则作用域
export const deleteSeatRuleScope = (ruleScopeId: number) => {
  return client.DELETE('/api/admin/seat-rule-scopes/{ruleScopeId}', {
    params: { path: { ruleScopeId } },
  });
};

// ========== 动态定价规则（管理端） ==========
export type CreateDynamicPricingRuleRequest = components['schemas']['CreateDynamicPricingRuleRequest'];

// 管理端：整体覆盖配置场次动态调价规则（传空数组即清空）
export const configureDynamicPricingRules = (sessionId: number, data: CreateDynamicPricingRuleRequest[]) => {
  return client.POST('/api/admin/sessions/{sessionId}/dynamic-pricing-rules', {
    params: { path: { sessionId } },
    body: data,
  });
};

// ========== 电子票核销（管理端） ==========
export type RedeemTicketRequest = components['schemas']['RedeemTicketRequest'];
export type TicketRedemption = components['schemas']['TicketRedemptionResponse'];

// 管理端：核销电子票
export const redeemTicket = (data: RedeemTicketRequest) => {
  return client.POST('/api/admin/tickets/redeem', { body: data });
};

// ========== 营销内容相关 ==========
export type MarketingContentType = components['schemas']['MarketingContentType'];
export type MarketingContentStatus = components['schemas']['MarketingContentStatus'];
export type MarketingContentDto = components['schemas']['MarketingContentDto'];
export type CreateMarketingContentRequest = components['schemas']['CreateMarketingContentRequest'];
export type UpdateMarketingContentRequest = components['schemas']['UpdateMarketingContentRequest'];

// 创建营销内容
export const createMarketingContent = (data: CreateMarketingContentRequest) => {
  return client.POST('/api/admin/marketing-contents', {
    body: data,
  });
};

// 更新营销内容
export const updateMarketingContent = (contentId: number, data: UpdateMarketingContentRequest) => {
  return client.PUT('/api/admin/marketing-contents/{contentId}', {
    params: { path: { contentId } },
    body: data,
  });
};

// 获取营销内容列表（管理端）
export const getMarketingContentList = (params?: {
  ShowId?: number;
  ContentType?: MarketingContentType;
  Status?: MarketingContentStatus;
  Keyword?: string;
  PageIndex?: number;
  PageSize?: number;
}) => {
  return client.GET('/api/admin/marketing-contents', {
    params: { query: params },
  });
};

// 删除营销内容
export const deleteMarketingContent = (contentId: number) => {
  return client.DELETE('/api/admin/marketing-contents/{contentId}', {
    params: { path: { contentId } },
  });
};

// ========== 用户管理（管理端） ==========
export type AdminUser = components['schemas']['AdminUserResponse'];
export type CreateAdminUserRequest = components['schemas']['RegisterRequest'];

// 管理端：分页查询用户列表（支持关键字/状态筛选）
export const getAdminUserList = (params?: {
  Keyword?: string;
  Status?: number;
  PageIndex?: number;
  PageSize?: number;
}) => {
  return client.GET('/api/admin/users', {
    params: { query: params },
  });
};

// 管理端：创建用户（默认绑定 USER 角色）
export const createAdminUser = (data: CreateAdminUserRequest) => {
  return client.POST('/api/admin/users', { body: data });
};

// 管理端：删除用户（仅无订单/票务/会话等关联数据时可删除）
export const deleteAdminUser = (userId: number) => {
  return client.DELETE('/api/admin/users/{userId}', {
    params: { path: { userId } },
  });
};
