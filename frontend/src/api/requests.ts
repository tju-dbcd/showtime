import { client } from './request';

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
    Status?: string;
  }) =>
    client.GET('/api/client/shows', { params: params as any }),

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
  getOrders: (params?: { Status?: string; Page?: number; PageSize?: number }) =>
    client.GET('/api/orders', { params: params as any }),

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
    client.POST('/api/orders', { body: data as any }),

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

  // payChannel: 'ALIPAY' | 'WECHAT' | 'UNIONPAY' | 'BALANCE'
  // result: 'SUCCESS' | 'FAIL'
  mockPayment: (orderId: number, data: { payChannel: string; result: string }) =>
    client.POST('/api/orders/{orderId}/payments/mock', {
      params: { path: { orderId } },
      body: data as any,
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
    client.PUT('/api/users/me/avatar', { body: data as any }),
};
