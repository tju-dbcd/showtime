// ========== 通用响应结构 ==========
export interface ApiResponse<T> {
  success: boolean;
  data: T | null;
  code: string | null;
  message: string;
}

// ========== 分页响应 ==========
export interface PagedResponse<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
}

// ========== 认证相关 ==========
export interface LoginRequest {
  account: string;
  password: string;
}

export interface LoginResponse {
  accessToken: string;
  tokenType: string;
  expiresIn: number;
  expiresAtUtc: string;
  user: UserResponse;
}

export interface RegisterRequest {
  userName: string;
  password: string;
  phone: string;
  nickname?: string | null;
  email?: string | null;
}

export interface RegisterResponse {
  user: UserResponse;
}

export interface UserResponse {
  userId: number;
  userName: string;
  nickname: string | null;
  phone: string;
  email: string | null;
  roles: string[];
}

// ========== 演出相关 ==========
export interface ShowSessionDto {
  showId: number;
  sessionId: number;
  startTime: string;
  endTime: string;
  saleStartTime: string;
  sessionStatus: string;
  seatMapId: number;
}

export interface PricingStrategyDto {
  priceStrategyId: number;
  seatSectionId: number;
  priceType: string;
  price: number;
  status: string;
}

// ========== 座位相关 ==========
export interface SessionSeatMapDto {
  sessionId: number;
  showId: number;
  seatMapId: number;
  startTime: string;
  endTime: string;
  saleStartTime: string;
  saleEndTime: string;
  sessionStatus: string;
  seatMap: SessionSeatMapMapDto;
}

export interface SessionSeatMapMapDto {
  seatMapId: number;
  venueId: number;
  mapCode: string;
  mapName: string;
  mapVersion: string;
  isDefault: boolean;
  mapWidth: number | null;
  mapHeight: number | null;
  mapStatus: string;
  sections: SessionSeatMapSectionDto[];
}

export interface SessionSeatMapSectionDto {
  seatSectionId: number;
  seatMapId: number;
  sectionCode: string;
  sectionName: string;
  sectionType: string;
  sectionColor: string | null;
  floorNo: string | null;
  isSellable: boolean;
  displayOrder: number;
  seats: SessionSeatMapSeatDto[];
}

export interface SessionSeatMapSeatDto {
  seatId: number;
  seatSectionId: number;
  rowCode: string;
  seatNo: string;
  rowIndex: number;
  colIndex: number;
  xCoord: number;
  yCoord: number;
  seatType: string;
  seatStatus: string;
  isAisleSide: boolean;
  isSellable: boolean;
  availabilityStatus: string;
}

// ========== 订单相关 ==========
export interface CreateOrderRequest {
  sessionId: number;
  items: CreateOrderItemRequest[];
  remark: string | null;
}

export interface CreateOrderItemRequest {
  seatId: number;
  priceStrategyId: number;
  realNameId: number | null;
}

export interface OrderResponse {
  orderId: number;
  orderNo: string;
  sessionId: number;
  totalAmount: number;
  discountAmount: number;
  ticketCount: number;
  orderStatus: string;
  expireTime: string;
  payTime: string | null;
  cancelTime: string | null;
  source: string;
  remark: string | null;
  items: OrderItemResponse[];
  payments: PaymentResponse[];
  tickets: ETicketSummaryResponse[];
}

export interface OrderSummaryResponse {
  orderId: number;
  orderNo: string;
  sessionId: number;
  totalAmount: number;
  discountAmount: number;
  ticketCount: number;
  orderStatus: string;
  expireTime: string;
  createTime: string;
}

export interface PagedOrderResponse {
  items: OrderSummaryResponse[];
  page: number;
  pageSize: number;
  totalCount: number;
}

export interface OrderItemResponse {
  orderItemId: number;
  seatId: number;
  priceStrategyId: number;
  realNameId: number | null;
  unitPrice: number;
  itemStatus: string;
}

// ========== 支付相关 ==========
export interface PaymentResponse {
  paymentId: number;
  paymentNo: string;
  orderId: number;
  payAmount: number;
  payChannel: string;
  payStatus: string;
  tradeNo: string | null;
  callbackTime: string | null;
  payTime: string | null;
}

export interface MockPaymentRequest {
  payChannel: string;
  result: string;
}

export interface ETicketSummaryResponse {
  eTicketId: number;
  eTicketNo: string;
  orderItemId: number;
  ticketStatus: string;
}

// ========== 枚举常量 ==========
export const OrderStatus = {
  Pending: 'Pending',
  Paid: 'Paid',
  Cancelled: 'Cancelled',
  Expired: 'Expired',
} as const;

export const PaymentStatus = {
  Pending: 'Pending',
  Success: 'Success',
  Failed: 'Failed',
} as const;

export const SessionStatus = {
  Draft: 'Draft',
  Published: 'Published',
  SoldOut: 'SoldOut',
  Ended: 'Ended',
  Cancelled: 'Cancelled',
} as const;

export const SeatAvailabilityStatus = {
  Available: 'Available',
  Locked: 'Locked',
  Sold: 'Sold',
} as const;
