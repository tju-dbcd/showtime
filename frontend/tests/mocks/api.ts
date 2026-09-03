/**
 * E2E API Mock —— 通过 page.route 拦截所有「路径以 /api/ 开头」的请求，
 * 即 http://127.0.0.1:5173/api/xxx；前端源码模块（如 /src/api/admin.ts）不受影响。
 *
 * 背景（PR42 评审遗留）：
 *  - 原 CI e2e job 直连生产后端（vite proxy 硬编码公网 IP），启用即污染生产库；
 *  - 本地/CI 均无独立测试库（后端硬编码 UseOracle，测试库为独立测试项目专用）。
 *
 * 方案：全量 mock /api 前缀的所有请求，前端业务流量不离开浏览器进程，零生产流量、
 * 零后端依赖、完全确定性。状态按「每个用例」隔离（每页新建 mock 数据库）。
 */
import type { Page, Route } from '@playwright/test';

// ========== 基础响应包装（与 backend ApiResponse 格式对齐） ==========
function ok<T>(data: T) {
  return { success: true, data, code: null as string | null, message: '' };
}

function fail(message: string, code = 'ERROR') {
  return { success: false, data: null, code, message };
}

const API_PREFIX = '/api/';

/** 从 URL 中截取 /api/ 之后的路径，例如 /api/client/shows/5 → client/shows/5 */
function apiPath(url: string): string {
  const idx = url.indexOf(API_PREFIX);
  if (idx < 0) return url;
  return url.slice(idx + API_PREFIX.length).split('?')[0];
}

// ========== 静态种子数据 ==========
const MOCK_SHOWS = [
  {
    showId: 1,
    showName: '「星光」演唱会之夜',
    categoryId: 1,
    description: 'E2E mock 演出数据',
    durationMinutes: 120,
    posterUrl: null,
    status: 'PUBLISHED',
    auditStatus: 'APPROVED',
    createTime: '2026-01-01T10:00:00',
  },
  {
    showId: 2,
    showName: '话剧《茶馆》',
    categoryId: 2,
    description: 'E2E mock 演出数据',
    durationMinutes: 150,
    posterUrl: null,
    status: 'PUBLISHED',
    auditStatus: 'APPROVED',
    createTime: '2026-01-02T10:00:00',
  },
];

const MOCK_SESSIONS = [
  {
    showId: 1,
    sessionId: 9001,
    startTime: '2026-12-31T19:30:00',
    endTime: '2026-12-31T21:30:00',
    saleStartTime: '2026-01-01T00:00:00',
    sessionStatus: 'ONSALE',
    seatMapId: 5001,
  },
  {
    showId: 1,
    sessionId: 9003,
    startTime: '2027-01-02T19:30:00',
    endTime: '2027-01-02T21:30:00',
    saleStartTime: '2026-01-01T00:00:00',
    sessionStatus: 'ONSALE',
    seatMapId: 5003,
  },
  {
    showId: 2,
    sessionId: 9002,
    startTime: '2026-12-30T19:30:00',
    endTime: '2026-12-30T21:30:00',
    saleStartTime: '2026-01-01T00:00:00',
    sessionStatus: 'ONSALE',
    seatMapId: 5002,
  },
];
const PRICING_STRATEGIES = [
  {
    priceStrategyId: 1,
    seatSectionId: 1001,
    priceType: 'STANDARD',
    price: 100,
    status: 'ACTIVE',
  },
];

/** 构造一张 5 排 × 8 座 的座位表，初始全部 AVAILABLE；sessionId 决定场次归属 */
function buildSeatMap(sessionId: number) {
  const rows = ['A', 'B', 'C', 'D', 'E'];
  const cols = 8;
  let seatId = 0;
  const session = MOCK_SESSIONS.find((s) => s.sessionId === sessionId) ?? MOCK_SESSIONS[0];
  const sections = [
    {
      seatSectionId: 1001,
      seatMapId: session.seatMapId,
      sectionCode: 'A1',
      sectionName: 'A区',
      sectionType: 'STANDARD',
      sectionColor: '#1677ff',
      floorNo: '1F',
      isSellable: true,
      displayOrder: 1,
      seats: rows.flatMap((row, ri) =>
        Array.from({ length: cols }, (_, ci) => ({
          seatId: ++seatId,
          seatSectionId: 1001,
          rowCode: row,
          seatNo: `${row}${ci + 1}`,
          rowIndex: ri,
          colIndex: ci,
          xCoord: ci * 10,
          yCoord: ri * 10,
          seatType: 'STANDARD',
          seatStatus: 'AVAILABLE',
          isAisleSide: ci === 3 || ci === 7,
          isSellable: true,
          availabilityStatus: 'AVAILABLE',
        })),
      ),
    },
  ];
  return {
    sessionId,
    showId: session.showId,
    seatMapId: session.seatMapId,
    startTime: session.startTime,
    endTime: session.endTime,
    saleStartTime: session.saleStartTime,
    saleEndTime: '2026-12-31T20:00:00',
    sessionStatus: session.sessionStatus,
    seatMap: {
      seatMapId: session.seatMapId,
      venueId: 1,
      mapCode: `MAP-${session.seatMapId}`,
      mapName: '主会场座位图',
      mapVersion: 'v1',
      isDefault: true,
      mapWidth: 80,
      mapHeight: 50,
      mapStatus: 'ACTIVE',
      sections,
    },
  };
}

const SEAT_MAPS: Record<number, ReturnType<typeof buildSeatMap>> = {
  9001: buildSeatMap(9001),
  9003: buildSeatMap(9003),
};

// ========== Mock 数据库（每个用例独立） ==========
interface MockOrder {
  orderId: number;
  orderNo: string;
  sessionId: number;
  totalAmount: number;
  discountAmount: number;
  ticketCount: number;
  orderStatus: string;
  expireTime: string;
  payTime: string | null;
  issueTime: string | null;
  cancelTime: string | null;
  source: string;
  remark: string | null;
  createTime: string;
  items: unknown[];
  payments: unknown[];
  tickets: unknown[];
}

interface MockDb {
  users: Map<string, { userName: string; password: string; phone: string; email: string | null; nickname: string | null }>;
  tokenOwner: Map<string, string>;
  nextOrderId: number;
  orders: MockOrder[];
  seatStatus: Map<number, string>; // seatId -> AVAILABLE | LOCKED | SOLD
  nextExchangeId: number;
  exchanges: unknown[]; // 改签申请（ExchangeSummaryResponse 形态）
  refunds: unknown[];
}

function createDb(): MockDb {
  return {
    users: new Map(),
    tokenOwner: new Map(),
    nextOrderId: 1,
    orders: [],
    seatStatus: new Map(),
    nextExchangeId: 1,
    exchanges: [],
    refunds: [],
  };
}

function getOrderOrNull(db: MockDb, orderId: number): MockOrder | null {
  return db.orders.find((o) => o.orderId === orderId) ?? null;
}

/** 校验改签 targetItems 是否符合后端 1:1 契约：数量与原票一致、ID 唯一且属于该订单 */
function validateExchangeTargetItems(db: MockDb, order: MockOrder, targetItems: unknown): string | null {
  const items = (targetItems as Array<{ originalOrderItemId: number }>) ?? [];
  const orderItemIds = (order.items as Array<{ orderItemId: number }>).map((it) => it.orderItemId);
  if (items.length !== order.items.length) {
    return '改签必须选择与原票数量一致的目标座位（1:1 映射）';
  }
  const seen = new Set<number>();
  for (const item of items) {
    const id = Number(item.originalOrderItemId);
    if (!id || !orderItemIds.includes(id)) {
      return '目标座位缺少有效的原票明细映射（originalOrderItemId 非法）';
    }
    if (seen.has(id)) {
      return '同一原票明细被映射到多个目标座位（重复 originalOrderItemId）';
    }
    seen.add(id);
  }
  return null;
}

// ========== 路由处理 ==========
export async function mockApi(page: Page): Promise<void> {
  const db = createDb();

  // 阻断一切非本机请求（头像占位图等外部资源会拖垮 networkidle，且测试不应依赖外网/外域）
  await page.route(
    (url) => url.hostname !== '127.0.0.1' && url.hostname !== 'localhost',
    (route) => route.fulfill({ status: 204, contentType: 'text/plain', body: '' }),
  );

  await page.route(
    (url) => url.pathname.startsWith('/api/'),
    async (route: Route) => {
    const request = route.request();
    const method = request.method();
    const path = apiPath(request.url());
    let rawBody: unknown = null;
    const postData = request.postData();
    if (postData) {
      try {
        rawBody = JSON.parse(postData);
      } catch {
        rawBody = null;
      }
    }
    const body = (rawBody ?? {}) as Record<string, unknown>;

    const fulfill = (status: number, payload: unknown) =>
      route.fulfill({
        status,
        contentType: 'application/json',
        body: JSON.stringify(payload),
      });

    // ---------- Auth ----------
    if (method === 'POST' && path === 'auth/register') {
      const userName = String(body.userName ?? '');
      const password = String(body.password ?? '');
      const phone = String(body.phone ?? '');
      if (!userName || !password || !phone) {
        return fulfill(400, fail('请填写完整的注册信息'));
      }
      if (db.users.has(userName)) {
        return fulfill(409, fail('用户名已存在'));
      }
      db.users.set(userName, {
        userName,
        password,
        phone,
        email: (body.email as string | null) ?? null,
        nickname: (body.nickname as string | null) ?? null,
      });
      return fulfill(201, ok({
        user: {
          userId: db.users.size,
          userName,
          nickname: null,
          phone,
          email: (body.email as string | null) ?? null,
          roles: ['User'],
        },
      }));
    }

    if (method === 'POST' && path === 'auth/login') {
      const account = String(body.account ?? '');
      const password = String(body.password ?? '');
      const user = db.users.get(account);
      if (!user || user.password !== password) {
        return fulfill(401, fail('用户名或密码错误', 'INVALID_CREDENTIALS'));
      }
      const token = `mock-token-${account}`;
      db.tokenOwner.set(token, account);
      return fulfill(200, ok({
        accessToken: token,
        tokenType: 'Bearer',
        expiresIn: 3600,
        expiresAtUtc: new Date(Date.now() + 3600 * 1000).toISOString(),
        user: {
          userId: Array.from(db.users.values()).indexOf(user) + 1,
          userName: user.userName,
          nickname: user.nickname,
          phone: user.phone,
          email: user.email,
          roles: ['User'],
        },
      }));
    }

    // ---------- 演出 / 场次 ----------
    if (method === 'GET' && path === 'client/shows') {
      const url = new URL(request.url());
      const keyword = (url.searchParams.get('Keyword') ?? '').trim();
      const items = keyword
        ? MOCK_SHOWS.filter((s) => s.showName.includes(keyword))
        : MOCK_SHOWS;
      return fulfill(200, ok({
        items,
        page: 1,
        pageSize: 20,
        totalCount: items.length,
      }));
    }

    const showById = path.match(/^client\/shows\/(\d+)$/);
    if (method === 'GET' && showById) {
      const show = MOCK_SHOWS.find((s) => s.showId === Number(showById[1]));
      if (!show) return fulfill(404, fail('演出不存在'));
      return fulfill(200, ok(show));
    }

    const showSessions = path.match(/^client\/shows\/(\d+)\/sessions$/);
    if (method === 'GET' && showSessions) {
      const sessions = MOCK_SESSIONS.filter(
        (s) => s.showId === Number(showSessions[1]),
      );
      return fulfill(200, ok(sessions));
    }

    const pricing = path.match(/^client\/sessions\/(\d+)\/pricing-strategies$/);
    if (method === 'GET' && pricing) {
      return fulfill(200, ok(PRICING_STRATEGIES));
    }

    // ---------- 座位图 / 锁座 ----------
    const seatMap = path.match(/^sessions\/(\d+)\/seat-map$/);
    if (method === 'GET' && seatMap) {
      const seatMapData = SEAT_MAPS[Number(seatMap[1])];
      if (!seatMapData) {
        return fulfill(404, fail('场次不存在'));
      }
      return fulfill(200, ok({
        ...seatMapData,
        seatMap: {
          ...seatMapData.seatMap,
          sections: seatMapData.seatMap.sections.map((section) => ({
            ...section,
            seats: section.seats.map((seat) => ({
              ...seat,
              availabilityStatus: db.seatStatus.get(seat.seatId) ?? 'AVAILABLE',
            })),
          })),
        },
      }));
    }

    const lockSeats = path.match(/^sessions\/(\d+)\/seat-locks$/);
    if (method === 'POST' && lockSeats) {
      const sessionId = Number(lockSeats[1]);
      const seatIds = (body.seatIds as number[]) ?? [];
      const locks = seatIds.map((seatId) => {
        db.seatStatus.set(seatId, 'LOCKED');
        return {
          seatId,
          lockToken: `lock-${sessionId}-${seatId}`,
          expireTime: new Date(Date.now() + 10 * 60 * 1000).toISOString(),
        };
      });
      return fulfill(200, ok({
        sessionId,
        expireTime: new Date(Date.now() + 10 * 60 * 1000).toISOString(),
        locks,
      }));
    }

    const releaseLocks = path.match(/^sessions\/(\d+)\/seat-locks\/release$/);
    if (method === 'POST' && releaseLocks) {
      const tokens = (body.lockTokens as string[]) ?? [];
      tokens.forEach((token) => {
        const match = token.match(/^lock-\d+-(\d+)$/);
        if (match && db.seatStatus.get(Number(match[1])) === 'LOCKED') {
          db.seatStatus.set(Number(match[1]), 'AVAILABLE');
        }
      });
      return fulfill(200, ok({ sessionId: Number(releaseLocks[1]), releasedCount: tokens.length }));
    }

    // ---------- 订单 ----------
    if (method === 'POST' && path === 'orders') {
      const sessionId = Number(body.sessionId ?? 0);
      const items = (body.items as Array<{ seatId: number; unitPrice?: number }>) ?? [];
      if (items.length === 0) {
        return fulfill(400, fail('请至少选择一个座位'));
      }
      const total = items.reduce((sum, it) => sum + (it.unitPrice ?? 0), 0);
      // 同一座位只能有一个未取消订单
      const conflict = db.orders.some((o) =>
        o.orderStatus !== 'CANCELLED' &&
        o.items.some((it) => (it as { seatId: number }).seatId === (items[0] as { seatId: number }).seatId),
      );
      if (conflict) {
        return fulfill(409, fail('座位已被锁定或售出', 'SEAT_CONFLICT'));
      }
      const orderId = db.nextOrderId++;
      const now = new Date();
      const expireTime = new Date(now.getTime() + 15 * 60 * 1000);
      const order: MockOrder = {
        orderId,
        orderNo: `SO${now.getTime()}${orderId}`,
        sessionId,
        totalAmount: total,
        discountAmount: 0,
        ticketCount: items.length,
        orderStatus: 'PENDING_PAY',
        expireTime: expireTime.toISOString(),
        payTime: null,
        issueTime: null,
        cancelTime: null,
        source: 'WEB',
        remark: null,
        createTime: now.toISOString(),
        items: items.map((it, idx) => ({
          orderItemId: orderId * 100 + idx + 1,
          seatId: it.seatId,
          priceStrategyId: 1,
          realNameId: null,
          unitPrice: it.unitPrice ?? 100,
          itemStatus: 'NORMAL',
        })),
        payments: [],
        tickets: [],
      };
      db.orders.push(order);
      items.forEach((it) => db.seatStatus.set(it.seatId, 'SOLD'));
      return fulfill(201, ok(order));
    }

    if (method === 'GET' && path === 'orders') {
      return fulfill(200, ok({
        items: db.orders
          .slice()
          .sort((a, b) => b.orderId - a.orderId)
          .map((o) => ({
            orderId: o.orderId,
            orderNo: o.orderNo,
            sessionId: o.sessionId,
            totalAmount: o.totalAmount,
            discountAmount: o.discountAmount,
            ticketCount: o.ticketCount,
            orderStatus: o.orderStatus,
            expireTime: o.expireTime,
            createTime: o.createTime,
          })),
        page: 1,
        pageSize: 10,
        totalCount: db.orders.length,
      }));
    }

    const orderById = path.match(/^orders\/(\d+)$/);
    if (method === 'GET' && orderById) {
      const order = getOrderOrNull(db, Number(orderById[1]));
      if (!order) return fulfill(404, fail('订单不存在'));
      return fulfill(200, ok(order));
    }

    const payments = path.match(/^orders\/(\d+)\/payments$/);
    if (method === 'GET' && payments) {
      const order = getOrderOrNull(db, Number(payments[1]));
      return fulfill(200, ok(order ? order.payments : []));
    }

    const mockPayment = path.match(/^orders\/(\d+)\/payments\/mock$/);
    if (method === 'POST' && mockPayment) {
      const orderId = Number(mockPayment[1]);
      const order = getOrderOrNull(db, orderId);
      if (!order) return fulfill(404, fail('订单不存在'));
      const payChannel = String(body.payChannel ?? 'WeChat');
      const now = new Date().toISOString();
      const payment = {
        paymentId: orderId * 10 + 1,
        paymentNo: `PAY${now.replace(/\D/g, '').slice(0, 14)}`,
        orderId,
        payAmount: order.totalAmount,
        payChannel,
        payStatus: 'SUCCESS',
        tradeNo: 'MOCK-TRADE-001',
        callbackTime: now,
        payTime: now,
      };
      order.payments = [payment];
      order.orderStatus = 'PAID';
      order.payTime = now;
      return fulfill(200, ok({
        payment,
        orderStatus: 'PAID',
        issuedTicketCount: 0,
      }));
    }

    const tickets = path.match(/^orders\/(\d+)\/tickets$/);
    if (method === 'GET' && tickets) {
      const order = getOrderOrNull(db, Number(tickets[1]));
      return fulfill(200, ok(order ? order.tickets : []));
    }

    // ---------- 退票 ----------
    const refundQuote = path.match(/^orders\/(\d+)\/refunds\/quote$/);
    if (method === 'POST' && refundQuote) {
      const order = getOrderOrNull(db, Number(refundQuote[1]));
      if (!order) return fulfill(404, fail('订单不存在'));
      const items = (order.items as Array<{ orderItemId: number; unitPrice: number }>);
      const totalBase = items.reduce((sum, it) => sum + it.unitPrice, 0);
      return fulfill(200, ok({
        quotedAt: new Date().toISOString(),
        orderId: order.orderId,
        orderItemBaseAmounts: items.map((it) => ({
          orderItemId: it.orderItemId,
          baseAmount: it.unitPrice,
          refundAmount: Math.round(it.unitPrice * 0.7 * 100) / 100,
          feeAmount: 0,
        })),
        totalBaseAmount: totalBase,
        feeRate: 0.3,
        appliedServiceFee: 0,
        actualRefund: Math.round(totalBase * 0.7 * 100) / 100,
        refundMode: 'FULL',
      }));
    }

    const applyRefund = path.match(/^orders\/(\d+)\/refunds$/);
    if (method === 'POST' && applyRefund) {
      const order = getOrderOrNull(db, Number(applyRefund[1]));
      if (!order) return fulfill(404, fail('订单不存在'));
      const reason = String(body.reason ?? '');
      if (!reason.trim()) return fulfill(400, fail('请填写退票原因'));
      const orderItemIds = ((body.orderItemIds as number[]) ?? []).map(Number);
      const valid = orderItemIds.every((id) =>
        (order.items as Array<{ orderItemId: number }>).some((it) => it.orderItemId === id));
      if (!valid) return fulfill(400, fail('退票明细不合法', 'REFUND_ITEM_NOT_ELIGIBLE'));
      const refund = {
        refundId: order.orderId * 1000 + 1,
        refundNo: `RF${Date.now()}`,
        orderId: order.orderId,
        orderItemIds,
        reason,
        refundType: order.items.length === orderItemIds.length ? 'FULL' : 'PART',
        approveStatus: 'PENDING',
        refundStatus: 'PENDING',
        estimatedRefund: order.totalAmount * 0.7,
        createTime: new Date().toISOString(),
      };
      db.refunds.push(refund);
      return fulfill(201, ok(refund));
    }

    // ---------- 改签 ----------
    const exchangeQuote = path.match(/^orders\/(\d+)\/exchanges\/quote$/);
    if (method === 'POST' && exchangeQuote) {
      const order = getOrderOrNull(db, Number(exchangeQuote[1]));
      if (!order) return fulfill(404, fail('订单不存在'));
      const targetItems = (body.targetItems as Array<{
        originalOrderItemId: number; seatId: number; priceStrategyId: number; lockToken: string;
      }>) ?? [];
      const invalid = validateExchangeTargetItems(db, order, targetItems);
      if (invalid) return fulfill(400, fail(invalid, 'EXCHANGE_ITEM_NOT_ELIGIBLE'));
      const origItems = order.items as Array<{ orderItemId: number; unitPrice: number }>;
      const origDeduction = targetItems.reduce((sum, item) => {
        const orig = origItems.find((it) => it.orderItemId === Number(item.originalOrderItemId));
        return sum + (orig?.unitPrice ?? 0);
      }, 0);
      const targetAmount = targetItems.length * 100;
      const exchangeFee = 20;
      return fulfill(200, ok({
        quotedAt: new Date().toISOString(),
        orderId: order.orderId,
        origSessionId: order.sessionId,
        targetSessionId: Number(body.targetSessionId ?? 0),
        origDeduction,
        targetAmount,
        priceDiff: targetAmount - origDeduction,
        exchangeFee,
        amountDue: targetAmount - origDeduction + exchangeFee,
        appliedPolicyId: 1,
        policyName: '默认政策',
        items: targetItems.map((item) => ({
          originalOrderItemId: Number(item.originalOrderItemId),
          targetSeatId: Number(item.seatId),
          targetPriceStrategyId: Number(item.priceStrategyId),
          realNameId: null,
          originalUnitPrice: origItems.find((it) => it.orderItemId === Number(item.originalOrderItemId))?.unitPrice ?? 0,
          newUnitPrice: 100,
        })),
      }));
    }

    const applyExchange = path.match(/^orders\/(\d+)\/exchanges$/);
    if (method === 'POST' && applyExchange) {
      const order = getOrderOrNull(db, Number(applyExchange[1]));
      if (!order) return fulfill(404, fail('订单不存在'));
      const targetItems = (body.targetItems as Array<{ originalOrderItemId: number }>) ?? [];
      const invalid = validateExchangeTargetItems(db, order, targetItems);
      if (invalid) return fulfill(400, fail(invalid, 'EXCHANGE_ITEM_NOT_ELIGIBLE'));
      const exchangeId = db.nextExchangeId++;
      const now = new Date();
      const summary = {
        exchangeId,
        exchangeNo: `EX${now.getTime()}${exchangeId}`,
        originalOrderId: order.orderId,
        childOrderId: order.orderId * 1000 + exchangeId,
        amountDue: targetItems.length * 100 - order.totalAmount + 20,
        approveStatus: 'PENDING',
        exchangeStatus: 'PENDING',
        expireTime: new Date(now.getTime() + 24 * 3600 * 1000).toISOString(),
        createTime: now.toISOString(),
        completeTime: null,
      };
      db.exchanges.push(summary);
      return fulfill(201, ok({
        exchangeId,
        exchangeNo: summary.exchangeNo,
        originalOrderId: order.orderId,
        childOrderId: summary.childOrderId,
        userId: 1,
        origSessionId: order.sessionId,
        targetSessionId: Number(body.targetSessionId ?? 0),
        reason: (body.reason as string | null) ?? null,
        origDeduction: order.totalAmount,
        targetAmount: targetItems.length * 100,
        priceDiff: targetItems.length * 100 - order.totalAmount,
        exchangeFee: 20,
        amountDue: summary.amountDue,
        appliedPolicyId: 1,
        policyName: '默认政策',
        approveStatus: 'PENDING',
        exchangeStatus: 'PENDING',
        reviewBy: null,
        reviewTime: null,
        reviewRemark: null,
        completeTime: null,
        expireTime: summary.expireTime,
        createTime: summary.createTime,
        items: [],
      }));
    }

    const exchangeList = path.match(/^orders\/(\d+)\/exchanges$/);
    if (method === 'GET' && exchangeList) {
      const orderId = Number(exchangeList[1]);
      const items = db.exchanges.filter((e) => (e as { originalOrderId: number }).originalOrderId === orderId);
      return fulfill(200, ok({ items, page: 1, pageSize: 20, totalCount: items.length }));
    }

    const payExchange = path.match(/^exchanges\/(\d+)\/pay$/);
    if (method === 'POST' && payExchange) {
      const exchangeId = Number(payExchange[1]);
      const exchange = db.exchanges.find((e) => (e as { exchangeId: number }).exchangeId === exchangeId);
      if (!exchange) return fulfill(404, fail('改签申请不存在'));
      (exchange as { approveStatus: string }).approveStatus = 'APPROVED';
      (exchange as { exchangeStatus: string }).exchangeStatus = 'COMPLETED';
      (exchange as { completeTime: string | null }).completeTime = new Date().toISOString();
      const now = new Date().toISOString();
      return fulfill(200, ok({
        payment: {
          paymentId: exchangeId * 10 + 7,
          paymentNo: `PAYEX${now.replace(/\D/g, '').slice(0, 14)}`,
          orderId: (exchange as { childOrderId: number }).childOrderId,
          payAmount: (exchange as { amountDue: number }).amountDue,
          payChannel: String(body.payChannel ?? 'WECHAT'),
          payStatus: 'SUCCESS',
          tradeNo: 'MOCK-EX-TRADE-001',
          callbackTime: now,
          payTime: now,
        },
        exchange,
      }));
    }

    // ---------- 兜底：未匹配端点返回空成功，避免页面报错 ----------
    return fulfill(200, ok(null));
  });
}