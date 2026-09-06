import * as signalR from '@microsoft/signalr';

// 订单实时通知（后端 OrderNotificationsHub + SignalR）：
// - OrderCreated：下单成功后推送给订单所属用户
// - RefundStatusChanged：退款审核通过(PROCESSING)/完成(COMPLETED)/拒绝(FAILED) 推送
// 连接按需建立（页面挂载时调用 ensureRealtimeConnection），断开后自动重连。

export interface OrderCreatedEvent {
  eventId: string;
  eventType: string;
  occurredAt: string;
  orderId: number;
  orderNo: string;
  userId: number;
  sessionId: number;
  totalAmount: number;
  ticketCount: number;
  orderStatus: string;
}

export interface RefundStatusChangedEvent {
  eventId: string;
  eventType: string;
  occurredAt: string;
  refundId: number;
  refundNo: string;
  orderId: number;
  userId: number;
  approveStatus: string;
  refundStatus: string;
  actualRefund: number | null;
}

type Unsubscribe = () => void;

const orderCreatedListeners = new Set<(event: OrderCreatedEvent) => void>();
const refundStatusListeners = new Set<(event: RefundStatusChangedEvent) => void>();

let connection: signalR.HubConnection | null = null;
let startPromise: Promise<void> | null = null;
let started = false;

function hubUrl(): string {
  // 与 API 同源：未配置 VITE_API_BASE_URL 时走 vite 代理（/hubs 已加 ws 代理）
  const base = import.meta.env.VITE_API_BASE_URL || '';
  return `${base}/hubs/order-notifications`;
}

function getConnection(): signalR.HubConnection {
  if (connection) {
    return connection;
  }

  connection = new signalR.HubConnectionBuilder()
    .withUrl(hubUrl(), {
      // 后端在 /hubs/order-notifications 从 query access_token 读取 JWT
      accessTokenFactory: () => localStorage.getItem('accessToken') || '',
      transport: signalR.HttpTransportType.WebSockets | signalR.HttpTransportType.LongPolling,
    })
    .withAutomaticReconnect()
    .configureLogging(signalR.LogLevel.Warning)
    .build();

  connection.on('OrderCreated', (event: OrderCreatedEvent) => {
    orderCreatedListeners.forEach((listener) => listener(event));
  });
  connection.on('RefundStatusChanged', (event: RefundStatusChangedEvent) => {
    refundStatusListeners.forEach((listener) => listener(event));
  });

  return connection;
}

/** 建立（或复用）订单通知连接；未登录时静默跳过，失败自动重试。 */
export function ensureRealtimeConnection(): Promise<void> {
  if (!localStorage.getItem('accessToken')) {
    return Promise.resolve();
  }
  if (started) {
    return startPromise ?? Promise.resolve();
  }

  started = true;
  const conn = getConnection();
  startPromise = conn.start().catch((error) => {
    started = false;
    startPromise = null;
    console.warn('SignalR 连接失败，稍后重试：', error);
  });
  return startPromise;
}

/** 主动断开（如退出登录）；幂等。 */
export async function disconnectRealtimeConnection(): Promise<void> {
  started = false;
  startPromise = null;
  const conn = connection;
  connection = null;
  if (conn) {
    await conn.stop();
  }
}

export function subscribeOrderCreated(
  listener: (event: OrderCreatedEvent) => void,
): Unsubscribe {
  orderCreatedListeners.add(listener);
  return () => orderCreatedListeners.delete(listener);
}

export function subscribeRefundStatusChanged(
  listener: (event: RefundStatusChangedEvent) => void,
): Unsubscribe {
  refundStatusListeners.add(listener);
  return () => refundStatusListeners.delete(listener);
}