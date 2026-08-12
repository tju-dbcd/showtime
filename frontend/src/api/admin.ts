import { client } from './request';
import type { components } from './types';

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
  Status?: string;
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
  PageIndex?: number;
  PageSize?: number;
  Keyword?: string;
}) => {
  return client.GET('/api/admin/seat-maps', {
    params: { query: params },
  });
};

// 获取某座位图下的票区列表
export const getSeatSections = (seatMapId: number, params?: {
  PageIndex?: number;
  PageSize?: number;
}) => {
  return client.GET('/api/admin/seat-maps/{seatMapId}/sections', {
    params: { path: { seatMapId }, query: params },
  });
};

// ========== 订单相关 ==========
export type OrderSummary = components['schemas']['OrderSummaryResponse'];
export type OrderDetail = components['schemas']['OrderResponse'];
export type OrderItem = components['schemas']['OrderItemResponse'];
export type Payment = components['schemas']['PaymentResponse'];

// 获取订单列表
export const getOrderList = (params?: {
  Status?: string;
  Page?: number;
  PageSize?: number;
}) => {
  return client.GET('/api/orders', {
    params: { query: params },
  });
};

// 获取订单详情
export const getOrderDetail = (orderId: number) => {
  return client.GET('/api/orders/{orderId}', {
    params: { path: { orderId } },
  });
};

// 取消订单
export const cancelOrder = (orderId: number) => {
  return client.PATCH('/api/orders/{orderId}/cancel', {
    params: { path: { orderId } },
  });
};

// 获取订单支付记录
export const getOrderPayments = (orderId: number) => {
  return client.GET('/api/orders/{orderId}/payments', {
    params: { path: { orderId } },
  });
};
