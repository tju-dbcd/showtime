import apiClient from './client';
import type {
  ApiResponse,
  LoginRequest,
  LoginResponse,
  RegisterRequest,
  RegisterResponse,
  ShowSessionDto,
  PricingStrategyDto,
  SessionSeatMapDto,
  CreateOrderRequest,
  OrderResponse,
  PagedOrderResponse,
  PaymentResponse,
  MockPaymentRequest,
  PagedResponse,
  ShowDto,
} from '@/types/api';

// ========== Auth API ==========
export const authAPI = {
  login: (data: LoginRequest) =>
    apiClient.post<ApiResponse<LoginResponse>>('/api/auth/login', data),

  register: (data: RegisterRequest) =>
    apiClient.post<ApiResponse<RegisterResponse>>('/api/auth/register', data),
};

// ========== ShowSession API ==========
export const showSessionAPI = {
  // 获取演出的所有场次
  getShowSessions: (showId: number) =>
    apiClient.get<ApiResponse<ShowSessionDto[]>>(`/api/client/shows/${showId}/sessions`),

  // 获取场次的定价策略
  getPricingStrategies: (sessionId: number) =>
    apiClient.get<ApiResponse<PricingStrategyDto[]>>(`/api/client/sessions/${sessionId}/pricing-strategies`),
};

export const showAPI = {
  // 获取演出列表（分页/搜索/筛选）
  getShows: (params?: {
    PageIndex?: number;
    PageSize?: number;
    Keyword?: string;
    CategoryId?: number;
    Status?: string;
  }) =>
    apiClient.get<ApiResponse<PagedResponse<ShowDto>>>('/api/client/shows', { params }),

  // 获取演出详情
  getShowDetail: (showId: number) =>
    apiClient.get<ApiResponse<ShowDto>>(`/api/client/shows/${showId}`),
};

// ========== Session API ==========
export const sessionAPI = {
  // 获取场次的座位图
  getSessionSeatMap: (sessionId: number) =>
    apiClient.get<ApiResponse<SessionSeatMapDto>>(`/api/sessions/${sessionId}/seat-map`),
};

// ========== Order API ==========
export const orderAPI = {
  // 获取订单列表（分页）
  getOrders: (params?: { Status?: string; Page?: number; PageSize?: number }) =>
    apiClient.get<ApiResponse<PagedOrderResponse>>('/api/orders', { params }),

  // 获取订单详情
  getOrder: (orderId: number) =>
    apiClient.get<ApiResponse<OrderResponse>>(`/api/orders/${orderId}`),

  // 创建订单
  createOrder: (data: CreateOrderRequest) =>
    apiClient.post<ApiResponse<OrderResponse>>('/api/orders', data),

  // 取消订单
  cancelOrder: (orderId: number) =>
    apiClient.patch<ApiResponse<OrderResponse>>(`/api/orders/${orderId}/cancel`),
};

// ========== Payment API ==========
export const paymentAPI = {
  // 获取订单的支付记录
  getPayments: (orderId: number) =>
    apiClient.get<ApiResponse<PaymentResponse[]>>(`/api/orders/${orderId}/payments`),

  // 模拟支付
  mockPayment: (orderId: number, data: MockPaymentRequest) =>
    apiClient.post<ApiResponse<PaymentResponse>>(`/api/orders/${orderId}/payments/mock`, data),
};
