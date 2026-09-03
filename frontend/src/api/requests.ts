import { client } from './request';
import type { components } from './types';

type ShowStatus = components['schemas']['ShowStatus'];
type OrderStatus = components['schemas']['OrderStatus'];
type PaymentChannel = components['schemas']['PaymentChannel'];
type PaymentResult = components['schemas']['PaymentResult'];

// ========== Auth API ==========
export const authAPI = {
  login: (data: { account: string; password: string }) =>
    client.POST('/api/auth/login', { body: data }),

  register: (data: {
    userName: string;
    password: string;
    phone: string;
    nickname?: string | null;
    email?: string | null;
  }) =>
    client.POST('/api/auth/register', { body: data }),
};

// ========== Show API ==========
export const showAPI = {
  getShows: (params?: {
    PageIndex?: number;
    PageSize?: number;
    Keyword?: string;
    CategoryId?: number;
    Status?: ShowStatus;
  }) =>
    client.GET('/api/client/shows', { params: { query: params } }),

  getShowDetail: (showId: number) =>
    client.GET('/api/client/shows/{showId}', { params: { path: { showId } } }),
};

// ========== ShowSession API ==========
export const showSessionAPI = {
  getShowSessions: (showId: number) =>
    client.GET('/api/client/shows/{showId}/sessions', { params: { path: { showId } } }),

  getPricingStrategies: (sessionId: number) =>
    client.GET('/api/client/sessions/{sessionId}/pricing-strategies', { params: { path: { sessionId } } }),
};

// ========== Session API ==========
export const sessionAPI = {
  getSessionSeatMap: (sessionId: number) =>
    client.GET('/api/sessions/{sessionId}/seat-map', { params: { path: { sessionId } } }),
};

// ========== Order API ==========
export const orderAPI = {
  getOrders: (params?: { Status?: OrderStatus; Page?: number; PageSize?: number }) =>
    client.GET('/api/orders', { params: { query: params } }),

  getOrder: (orderId: number) =>
    client.GET('/api/orders/{orderId}', { params: { path: { orderId } } }),

  createOrder: (data: {
    sessionId: number;
    items: Array<{
      seatId: number;
      priceStrategyId: number;
      realNameId: number | null;
      lockToken: string;
    }>;
    remark: string | null;
  }) =>
    client.POST('/api/orders', { body: data }),

  cancelOrder: (orderId: number) =>
    client.PATCH('/api/orders/{orderId}/cancel', { params: { path: { orderId } } }),
};

// ========== Ticket API ==========
export const ticketAPI = {
  getTickets: (orderId: number) =>
    client.GET('/api/orders/{orderId}/tickets', { params: { path: { orderId } } }),
};

// ========== Payment API ==========
export const paymentAPI = {
  getPayments: (orderId: number) =>
    client.GET('/api/orders/{orderId}/payments', { params: { path: { orderId } } }),

  // payChannel/result 与后端 PaymentChannel/PaymentResult 枚举一致
  mockPayment: (orderId: number, data: { payChannel: PaymentChannel; result: PaymentResult }) =>
    client.POST('/api/orders/{orderId}/payments/mock', {
      params: { path: { orderId } },
      body: data,
    }),
};

// ========== 座位锁 API ==========
export const seatLockAPI = {
  lockSeats: (sessionId: number, seatIds: number[]) =>
    client.POST('/api/sessions/{sessionId}/seat-locks', {
      params: { path: { sessionId } },
      body: { seatIds },
    }),

  releaseSeats: (sessionId: number, lockTokens: string[]) =>
    client.POST('/api/sessions/{sessionId}/seat-locks/release', {
      params: { path: { sessionId } },
      body: { lockTokens },
    }),
};

// ========== 用户 API ==========
export const userAPI = {
  // 更新头像
  updateAvatar: (data: { avatarUrl: string }) =>
    client.PUT('/api/users/me/avatar', { body: data }),
};

// ========== 退票 API ==========
export const refundAPI = {
  // 退票报价（获取退票金额估算）
  getRefundQuote: (orderId: number, data: { orderItemIds: number[] }) =>
    client.POST('/api/orders/{orderId}/refunds/quote', {
      params: { path: { orderId } },
      body: data,
    }),

  // 申请退票
  applyRefund: (orderId: number, data: { orderItemIds: number[]; reason: string }) =>
    client.POST('/api/orders/{orderId}/refunds', {
      params: { path: { orderId } },
      body: data,
    }),

  // 获取退票记录列表（使用枚举类型）
  getRefundList: (
    orderId: number,
    params?: {
      ApproveStatus?: 'PENDING' | 'APPROVED' | 'REJECTED';
      RefundStatus?: 'PENDING' | 'PROCESSING' | 'COMPLETED' | 'FAILED';
      Page?: number;
      PageSize?: number;
    }
  ) =>
    client.GET('/api/orders/{orderId}/refunds', {
      params: { path: { orderId }, query: params },
    }),

  // 获取退票详情
  getRefundDetail: (refundId: number) =>
    client.GET('/api/refunds/{refundId}', {
      params: { path: { refundId } },
    }),
};

// ========== 改签 API ==========
export const exchangeAPI = {
  // 改签报价（获取改签估算）
  getExchangeQuote: (
    orderId: number,
    data: {
      targetSessionId: number;
      targetItems: {
        originalOrderItemId: number;
        seatId: number;
        priceStrategyId: number;
        lockToken: string;
      }[];
    }
  ) =>
    client.POST('/api/orders/{orderId}/exchanges/quote', {
      params: { path: { orderId } },
      body: data,
    }),

  // 申请改签
  applyExchange: (
    orderId: number,
    data: {
      targetSessionId: number;
      targetItems: {
        originalOrderItemId: number;
        seatId: number;
        priceStrategyId: number;
        lockToken: string;
      }[];
      reason: string | null;
    }
  ) =>
    client.POST('/api/orders/{orderId}/exchanges', {
      params: { path: { orderId } },
      body: data,
    }),

  // 获取改签记录列表（使用枚举类型）
  getExchangeList: (
    orderId: number,
    params?: {
      ApproveStatus?: 'PENDING' | 'APPROVED' | 'REJECTED';
      ExchangeStatus?: 'PENDING' | 'PROCESSING' | 'COMPLETED' | 'FAILED';
      Page?: number;
      PageSize?: number;
    }
  ) =>
    client.GET('/api/orders/{orderId}/exchanges', {
      params: { path: { orderId }, query: params },
    }),

  // 获取改签详情
  getExchangeDetail: (exchangeId: number) =>
    client.GET('/api/exchanges/{exchangeId}', {
      params: { path: { exchangeId } },
    }),

  // 支付改签差价（管理员审核通过后调用，body 与 ExchangePaymentRequest 契约一致）
  payExchange: (
    exchangeId: number,
    data: { payChannel: PaymentChannel; result: PaymentResult }
  ) =>
    client.POST('/api/exchanges/{exchangeId}/pay', {
      params: { path: { exchangeId } },
      body: data,
    }),
};