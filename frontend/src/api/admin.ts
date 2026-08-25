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

// 更新演出
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

// 给场次添加定价策略（数组）
export const addPricingStrategies = (sessionId: number, data: CreatePriceStrategyRequest[]) => {
  return client.POST('/api/admin/sessions/{sessionId}/pricing-strategies', {
    params: { path: { sessionId } },
    body: data,
  });
};

// 更新场次状态
export const updateSessionStatus = (sessionId: number, data: UpdateSessionStatusRequest) => {
  return client.PUT('/api/admin/sessions/{sessionId}/status', {
    params: { path: { sessionId } },
    body: data,
  });
};

// ========== 座位图/票区相关 ==========
export type SeatMapResponse = components['schemas']['SeatMapResponse'];
export type SeatSectionResponse = components['schemas']['SeatSectionResponse'];

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
