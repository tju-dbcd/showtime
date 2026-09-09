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
          xCoord: 60 + ci * 40,
          // 第一排从 y=50 开始，用于回归验证：若不避让舞台/标注带，前排会与顶部装饰层重合
          yCoord: 50 + ri * 40,
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
      mapWidth: 420,
      mapHeight: 300,
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
interface MockUser {
  userName: string;
  password: string;
  phone: string;
  email: string | null;
  nickname: string | null;
  roles: string[];
}

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

export interface MockDb {
  users: Map<string, MockUser>;
  tokenOwner: Map<string, string>;
  nextOrderId: number;
  orders: MockOrder[];
  seatStatus: Map<number, string>; // seatId -> AVAILABLE | LOCKED | SOLD
  nextExchangeId: number;
  exchanges: unknown[]; // 改签申请（ExchangeSummaryResponse 形态）
  refunds: unknown[]; // 退票申请（RefundSummaryResponse 形态）
  refundPolicies: unknown[]; // 退票策略
  exchangePolicies: unknown[]; // 改签策略
  nextSeatRuleId: number;
  seatRules: unknown[]; // 座位规则（SeatRuleResponse 形态）
  scopeRules: unknown[]; // 座位规则作用域
}

function createDb(): MockDb {
  return {
    users: new Map([
      [
        'admin',
        {
          userName: 'admin',
          password: 'admin123',
          phone: '13800000000',
          email: null,
          nickname: '系统管理员',
          roles: ['Admin'],
        },
      ],
    ]),
    tokenOwner: new Map(),
    nextOrderId: 1,
    orders: [],
    seatStatus: new Map(),
    nextExchangeId: 1,
    exchanges: [],
    refunds: [],
    refundPolicies: [],
    exchangePolicies: [],
    nextSeatRuleId: 1,
    seatRules: [],
    scopeRules: [],
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
/**
 * mockApi 拦截全部 /api/ 请求。可选 seed 回调用于在用例内预置管理端数据。
 */
export async function mockApi(page: Page, seed?: (db: MockDb) => void): Promise<void> {
  const db = createDb();
  seed?.(db);

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
        roles: ['User'],
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
          roles: user.roles,
        },
      }));
    }

    // ---------- 演出 / 场次 ----------
    if (method === 'GET' && path === 'categories') {
      return fulfill(200, ok([
        { categoryId: 1, categoryName: '演唱会', parentId: null, sortOrder: 1 },
        { categoryId: 2, categoryName: '话剧', parentId: null, sortOrder: 2 },
      ]));
    }

    if (method === 'GET' && path === 'client/shows') {
      const url = new URL(request.url());
      const keyword = (url.searchParams.get('Keyword') ?? '').trim();
      const categoryId = Number(url.searchParams.get('CategoryId') ?? 0);
      const items = keyword
        ? MOCK_SHOWS.filter((s) => s.showName.includes(keyword))
        : MOCK_SHOWS.filter((s) => !categoryId || s.categoryId === categoryId);
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
      const url = new URL(request.url());
      const status = url.searchParams.get('Status');
      const filteredOrders = db.orders.filter((order) => !status || order.orderStatus === status);
      return fulfill(200, ok({
        items: filteredOrders
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
        totalCount: filteredOrders.length,
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

    // ---------- 管理端：场馆 / 演出 / 场次 / 座位图 ----------
    if (method === 'GET' && path === 'admin/venues') {
      return fulfill(200, ok([
        { venueId: 1, venueName: '主会场', address: null, contactPhone: null, status: 'ENABLED', remark: null },
      ]));
    }

    if (method === 'GET' && path === 'admin/shows') {
      return fulfill(200, ok({
        items: MOCK_SHOWS.map((s) => ({
          ...s,
          auditStatus: s.auditStatus ?? 'APPROVED',
        })),
        page: 1,
        pageSize: 100,
        totalCount: MOCK_SHOWS.length,
      }));
    }

    const adminShowSessions = path.match(/^admin\/shows\/(\d+)\/sessions$/);
    if (method === 'GET' && adminShowSessions) {
      const sessions = MOCK_SESSIONS.filter((s) => s.showId === Number(adminShowSessions[1]));
      return fulfill(200, ok(sessions));
    }

    const adminSeatMaps = Object.values(SEAT_MAPS).map((s) => ({
      seatMapId: s.seatMap.seatMapId,
      venueId: s.seatMap.venueId,
      venueName: '主会场',
      mapCode: s.seatMap.mapCode,
      mapName: s.seatMap.mapName,
      mapVersion: s.seatMap.mapVersion,
      isDefault: s.seatMap.isDefault,
      mapWidth: null,
      mapHeight: null,
      mapStatus: s.seatMap.mapStatus,
      remark: null,
    }));

    if (method === 'GET' && path === 'admin/seat-maps') {
      return fulfill(200, ok({
        items: adminSeatMaps,
        page: 1,
        pageSize: 100,
        totalCount: adminSeatMaps.length,
      }));
    }

    const adminSeatMapSections = path.match(/^admin\/seat-maps\/(\d+)\/sections$/);
    if (method === 'GET' && adminSeatMapSections) {
      const map = Object.values(SEAT_MAPS).find((s) => s.seatMap.seatMapId === Number(adminSeatMapSections[1]));
      const items = (map?.seatMap.sections ?? []).map((sec) => ({
        seatSectionId: sec.seatSectionId,
        seatMapId: sec.seatMapId,
        sectionCode: sec.sectionCode,
        sectionName: sec.sectionName,
        sectionType: sec.sectionType,
        sectionColor: sec.sectionColor,
        floorNo: sec.floorNo,
        isSellable: sec.isSellable,
        displayOrder: sec.displayOrder,
        remark: null,
      }));
      return fulfill(200, ok({ items, page: 1, pageSize: 100, totalCount: items.length }));
    }

    const dynamicPricing = path.match(/^admin\/sessions\/(\d+)\/dynamic-pricing-rules$/);
    if (method === 'POST' && dynamicPricing) {
      const rules = (Array.isArray(rawBody) ? rawBody : []) as Array<Record<string, unknown>>;
      // 对齐后端 ShowSessionImplement 校验：时间窗口为开演前分钟数，起始必须 >= 结束
      const invalid = rules.find((r) =>
        r.startOffsetMinutes != null && r.endOffsetMinutes != null && Number(r.startOffsetMinutes) < Number(r.endOffsetMinutes),
      );
      if (invalid) {
        return fulfill(400, fail(`调价时间窗口配置无效：StartOffsetMinutes (${invalid.startOffsetMinutes}) 必须大于等于 EndOffsetMinutes (${invalid.endOffsetMinutes})`));
      }
      return fulfill(200, ok(null));
    }

    // ---------- 管理端：订单 ----------
    if (method === 'GET' && path === 'admin/orders') {
      const url = new URL(request.url());
      const status = url.searchParams.get('Status');
      const keyword = (url.searchParams.get('Keyword') ?? '').trim();
      let orders = db.orders.slice().sort((a, b) => b.orderId - a.orderId);
      if (status) orders = orders.filter((o) => o.orderStatus === status);
      if (keyword) orders = orders.filter((o) => o.orderNo.includes(keyword) || o.userName?.includes(keyword));
      return fulfill(200, ok({
        items: orders.map((o) => ({
          orderId: o.orderId,
          orderNo: o.orderNo,
          userId: 1,
          userName: 'admin',
          nickname: '系统管理员',
          phone: '13800000000',
          sessionId: o.sessionId,
          orderType: 'NORMAL',
          parentOrderId: null,
          totalAmount: o.totalAmount,
          discountAmount: o.discountAmount,
          ticketCount: o.ticketCount,
          orderStatus: o.orderStatus,
          canPay: o.orderStatus === 'PENDING_PAY',
          canCancel: o.orderStatus === 'PENDING_PAY',
          expireTime: o.expireTime,
          createTime: o.createTime,
        })),
        page: 1,
        pageSize: 10,
        totalCount: orders.length,
      }));
    }

    const adminOrderById = path.match(/^admin\/orders\/(\d+)$/);
    if (method === 'GET' && adminOrderById) {
      const order = getOrderOrNull(db, Number(adminOrderById[1]));
      if (!order) return fulfill(404, fail('订单不存在'));
      return fulfill(200, ok({
        orderId: order.orderId,
        orderNo: order.orderNo,
        sessionId: order.sessionId,
        orderType: 'NORMAL',
        parentOrderId: null,
        totalAmount: order.totalAmount,
        discountAmount: order.discountAmount,
        ticketCount: order.ticketCount,
        orderStatus: order.orderStatus,
        expireTime: order.expireTime,
        payTime: order.payTime,
        issueTime: order.issueTime,
        cancelTime: order.cancelTime,
        source: order.source,
        remark: order.remark,
        createTime: order.createTime,
        items: order.items,
        tickets: order.tickets,
      }));
    }

    const adminOrderIssue = path.match(/^admin\/orders\/(\d+)\/issue$/);
    if (method === 'POST' && adminOrderIssue) {
      const order = getOrderOrNull(db, Number(adminOrderIssue[1]));
      if (!order) return fulfill(404, fail('订单不存在'));
      const now = new Date().toISOString();
      order.orderStatus = 'ISSUED';
      order.issueTime = now;
      const tickets = (order.items as Array<{ orderItemId: number }>).map((it, idx) => ({
        eTicketId: order.orderId * 10000 + idx + 1,
        eTicketNo: `ET${now.replace(/\D/g, '').slice(0, 12)}${idx}`,
        orderId: order.orderId,
        orderItemId: it.orderItemId,
        ticketStatus: 'UNUSED',
        qrCode: `mock-qr-${order.orderId}-${idx}`,
      }));
      order.tickets = tickets;
      return fulfill(200, ok({
        orderId: order.orderId,
        orderStatus: 'ISSUED',
        createdTicketCount: tickets.length,
        existingTicketCount: 0,
        totalTicketCount: tickets.length,
        issueTime: now,
      }));
    }

    const adminOrderCancel = path.match(/^admin\/orders\/(\d+)\/cancel$/);
    if (method === 'PATCH' && adminOrderCancel) {
      const order = getOrderOrNull(db, Number(adminOrderCancel[1]));
      if (!order) return fulfill(404, fail('订单不存在'));
      order.orderStatus = 'CANCELLED';
      order.cancelTime = new Date().toISOString();
      return fulfill(200, ok(null));
    }

    // ---------- 管理端：退票审核 ----------
    if (method === 'GET' && path === 'admin/refunds') {
      const url = new URL(request.url());
      let items = (db.refunds as Array<Record<string, unknown>>).slice();
      const approve = url.searchParams.get('ApproveStatus');
      const refundStatus = url.searchParams.get('RefundStatus');
      const refundNo = url.searchParams.get('RefundNo');
      const orderId = url.searchParams.get('OrderId');
      if (approve) items = items.filter((r) => r.approveStatus === approve);
      if (refundStatus) items = items.filter((r) => r.refundStatus === refundStatus);
      if (refundNo) items = items.filter((r) => String(r.refundNo).includes(refundNo));
      if (orderId) items = items.filter((r) => Number(r.orderId) === Number(orderId));
      return fulfill(200, ok({
        items: items.map((r) => ({
          refundId: r.refundId,
          refundNo: r.refundNo,
          orderId: r.orderId,
          refundType: r.refundType,
          actualRefund: r.actualRefund,
          approveStatus: r.approveStatus,
          refundStatus: r.refundStatus,
          createTime: r.createTime,
          completeTime: r.completeTime ?? null,
        })),
        page: 1,
        pageSize: 20,
        totalCount: items.length,
      }));
    }

    const adminRefundById = path.match(/^admin\/refunds\/(\d+)$/);
    if (method === 'GET' && adminRefundById) {
      const refund = (db.refunds as Array<Record<string, unknown>>).find((r) => Number(r.refundId) === Number(adminRefundById[1]));
      if (!refund) return fulfill(404, fail('退票申请不存在'));
      return fulfill(200, ok(refund));
    }

    const adminRefundApprove = path.match(/^admin\/refunds\/(\d+)\/approve$/);
    if (method === 'POST' && adminRefundApprove) {
      const refund = (db.refunds as Array<Record<string, unknown>>).find((r) => Number(r.refundId) === Number(adminRefundApprove[1]));
      if (!refund) return fulfill(404, fail('退票申请不存在'));
      refund.approveStatus = 'APPROVED';
      refund.reviewBy = 'admin';
      refund.reviewTime = new Date().toISOString();
      refund.reviewRemark = (body.remark as string | null) ?? null;
      return fulfill(200, ok(refund));
    }

    const adminRefundReject = path.match(/^admin\/refunds\/(\d+)\/reject$/);
    if (method === 'POST' && adminRefundReject) {
      const refund = (db.refunds as Array<Record<string, unknown>>).find((r) => Number(r.refundId) === Number(adminRefundReject[1]));
      if (!refund) return fulfill(404, fail('退票申请不存在'));
      refund.approveStatus = 'REJECTED';
      refund.refundStatus = 'FAILED';
      refund.reviewBy = 'admin';
      refund.reviewTime = new Date().toISOString();
      refund.reviewRemark = (body.remark as string | null) ?? null;
      return fulfill(200, ok(refund));
    }

    // ---------- 管理端：改签审核 ----------
    if (method === 'GET' && path === 'admin/exchanges') {
      const url = new URL(request.url());
      let items = (db.exchanges as Array<Record<string, unknown>>).slice();
      const approve = url.searchParams.get('ApproveStatus');
      const exchangeStatus = url.searchParams.get('ExchangeStatus');
      const exchangeNo = url.searchParams.get('ExchangeNo');
      const orderId = url.searchParams.get('OriginalOrderId');
      if (approve) items = items.filter((r) => r.approveStatus === approve);
      if (exchangeStatus) items = items.filter((r) => r.exchangeStatus === exchangeStatus);
      if (exchangeNo) items = items.filter((r) => String(r.exchangeNo).includes(exchangeNo));
      if (orderId) items = items.filter((r) => Number(r.originalOrderId) === Number(orderId));
      return fulfill(200, ok({
        items: items.map((r) => ({
          exchangeId: r.exchangeId,
          exchangeNo: r.exchangeNo,
          originalOrderId: r.originalOrderId,
          childOrderId: r.childOrderId,
          amountDue: r.amountDue,
          approveStatus: r.approveStatus,
          exchangeStatus: r.exchangeStatus,
          expireTime: r.expireTime,
          createTime: r.createTime,
          completeTime: r.completeTime ?? null,
        })),
        page: 1,
        pageSize: 20,
        totalCount: items.length,
      }));
    }

    const adminExchangeById = path.match(/^admin\/exchanges\/(\d+)$/);
    if (method === 'GET' && adminExchangeById) {
      const exchange = (db.exchanges as Array<Record<string, unknown>>).find((r) => Number(r.exchangeId) === Number(adminExchangeById[1]));
      if (!exchange) return fulfill(404, fail('改签申请不存在'));
      return fulfill(200, ok(exchange));
    }

    const adminExchangeApprove = path.match(/^admin\/exchanges\/(\d+)\/approve$/);
    if (method === 'POST' && adminExchangeApprove) {
      const exchange = (db.exchanges as Array<Record<string, unknown>>).find((r) => Number(r.exchangeId) === Number(adminExchangeApprove[1]));
      if (!exchange) return fulfill(404, fail('改签申请不存在'));
      exchange.approveStatus = 'APPROVED';
      exchange.reviewBy = 'admin';
      exchange.reviewTime = new Date().toISOString();
      exchange.reviewRemark = (body.remark as string | null) ?? null;
      return fulfill(200, ok(exchange));
    }

    const adminExchangeReject = path.match(/^admin\/exchanges\/(\d+)\/reject$/);
    if (method === 'POST' && adminExchangeReject) {
      const exchange = (db.exchanges as Array<Record<string, unknown>>).find((r) => Number(r.exchangeId) === Number(adminExchangeReject[1]));
      if (!exchange) return fulfill(404, fail('改签申请不存在'));
      exchange.approveStatus = 'REJECTED';
      exchange.exchangeStatus = 'FAILED';
      exchange.reviewBy = 'admin';
      exchange.reviewTime = new Date().toISOString();
      exchange.reviewRemark = (body.remark as string | null) ?? null;
      return fulfill(200, ok(exchange));
    }

    // ---------- 管理端：退票策略 ----------
    if (method === 'GET' && path === 'admin/refund-policies') {
      const url = new URL(request.url());
      let items = (db.refundPolicies as Array<Record<string, unknown>>).slice();
      const showId = url.searchParams.get('ShowId');
      const status = url.searchParams.get('Status');
      if (showId) items = items.filter((p) => Number(p.showId) === Number(showId));
      if (status) items = items.filter((p) => Number(p.status) === Number(status));
      return fulfill(200, ok({
        items,
        page: 1,
        pageSize: 10,
        totalCount: items.length,
      }));
    }

    if (method === 'POST' && path === 'admin/refund-policies') {
      const now = new Date().toISOString();
      const policy = {
        policyId: db.refundPolicies.length + 1,
        showId: (body.showId as number | null) ?? null,
        policyName: String(body.policyName ?? ''),
        refundDeadlineHour: body.refundDeadlineHour ?? 2,
        refundRate: body.refundRate ?? 0.8,
        serviceFee: body.serviceFee ?? 0,
        priority: body.priority ?? 0,
        status: 1,
        remark: (body.remark as string | null) ?? null,
        createTime: now,
        updateTime: now,
      };
      db.refundPolicies.push(policy);
      return fulfill(201, ok(policy));
    }

    const adminRefundPolicyById = path.match(/^admin\/refund-policies\/(\d+)$/);
    if (method === 'PUT' && adminRefundPolicyById) {
      const policy = (db.refundPolicies as Array<Record<string, unknown>>).find((p) => Number(p.policyId) === Number(adminRefundPolicyById[1]));
      if (!policy) return fulfill(404, fail('策略不存在'));
      Object.assign(policy, {
        showId: (body.showId as number | null) ?? null,
        policyName: String(body.policyName ?? policy.policyName),
        refundDeadlineHour: body.refundDeadlineHour ?? policy.refundDeadlineHour,
        refundRate: body.refundRate ?? policy.refundRate,
        serviceFee: body.serviceFee ?? policy.serviceFee,
        priority: body.priority ?? policy.priority,
        remark: (body.remark as string | null) ?? policy.remark,
        updateTime: new Date().toISOString(),
      });
      return fulfill(200, ok(policy));
    }

    const adminRefundPolicyStatus = path.match(/^admin\/refund-policies\/(\d+)\/status$/);
    if (method === 'PATCH' && adminRefundPolicyStatus) {
      const policy = (db.refundPolicies as Array<Record<string, unknown>>).find((p) => Number(p.policyId) === Number(adminRefundPolicyStatus[1]));
      if (!policy) return fulfill(404, fail('策略不存在'));
      policy.status = body.status ?? policy.status;
      return fulfill(200, ok(policy));
    }

    // ---------- 管理端：改签策略 ----------
    if (method === 'GET' && path === 'admin/exchange-policies') {
      const url = new URL(request.url());
      let items = (db.exchangePolicies as Array<Record<string, unknown>>).slice();
      const showId = url.searchParams.get('ShowId');
      const status = url.searchParams.get('Status');
      if (showId) items = items.filter((p) => Number(p.showId) === Number(showId));
      if (status) items = items.filter((p) => Number(p.status) === Number(status));
      return fulfill(200, ok({
        items,
        page: 1,
        pageSize: 10,
        totalCount: items.length,
      }));
    }

    if (method === 'POST' && path === 'admin/exchange-policies') {
      const now = new Date().toISOString();
      const policy = {
        policyId: db.exchangePolicies.length + 1,
        showId: (body.showId as number | null) ?? null,
        policyName: String(body.policyName ?? ''),
        exchangeDeadlineHour: body.exchangeDeadlineHour ?? 3,
        exchangeFee: body.exchangeFee ?? 20,
        allowCrossSession: body.allowCrossSession ?? 0,
        priority: body.priority ?? 0,
        status: 1,
        remark: (body.remark as string | null) ?? null,
        createTime: now,
        updateTime: now,
      };
      db.exchangePolicies.push(policy);
      return fulfill(201, ok(policy));
    }

    const adminExchangePolicyById = path.match(/^admin\/exchange-policies\/(\d+)$/);
    if (method === 'PUT' && adminExchangePolicyById) {
      const policy = (db.exchangePolicies as Array<Record<string, unknown>>).find((p) => Number(p.policyId) === Number(adminExchangePolicyById[1]));
      if (!policy) return fulfill(404, fail('策略不存在'));
      Object.assign(policy, {
        showId: (body.showId as number | null) ?? null,
        policyName: String(body.policyName ?? policy.policyName),
        exchangeDeadlineHour: body.exchangeDeadlineHour ?? policy.exchangeDeadlineHour,
        exchangeFee: body.exchangeFee ?? policy.exchangeFee,
        allowCrossSession: body.allowCrossSession ?? policy.allowCrossSession,
        priority: body.priority ?? policy.priority,
        remark: (body.remark as string | null) ?? policy.remark,
        updateTime: new Date().toISOString(),
      });
      return fulfill(200, ok(policy));
    }

    const adminExchangePolicyStatus = path.match(/^admin\/exchange-policies\/(\d+)\/status$/);
    if (method === 'PATCH' && adminExchangePolicyStatus) {
      const policy = (db.exchangePolicies as Array<Record<string, unknown>>).find((p) => Number(p.policyId) === Number(adminExchangePolicyStatus[1]));
      if (!policy) return fulfill(404, fail('策略不存在'));
      policy.status = body.status ?? policy.status;
      return fulfill(200, ok(policy));
    }

    // ---------- 管理端：座位规则 ----------
    if (method === 'GET' && path === 'admin/seat-rules') {
      const url = new URL(request.url());
      let items = (db.seatRules as Array<Record<string, unknown>>).slice();
      const ruleType = url.searchParams.get('RuleType');
      const ruleStatus = url.searchParams.get('RuleStatus');
      if (ruleType) items = items.filter((p) => p.ruleType === ruleType);
      if (ruleStatus) items = items.filter((p) => p.ruleStatus === ruleStatus);
      return fulfill(200, ok({
        items,
        page: 1,
        pageSize: 10,
        totalCount: items.length,
      }));
    }

    if (method === 'POST' && path === 'admin/seat-rules') {
      const rule = {
        seatRuleId: db.nextSeatRuleId++,
        ruleCode: String(body.ruleCode ?? ''),
        ruleName: String(body.ruleName ?? ''),
        ruleType: String(body.ruleType ?? 'LIMIT_COUNT'),
        minSeatCount: body.minSeatCount ?? 1,
        maxSeatCount: body.maxSeatCount ?? 9,
        allowCrossRow: body.allowCrossRow ?? false,
        allowCrossSection: body.allowCrossSection ?? false,
        priority: body.priority ?? 0,
        ruleStatus: String(body.ruleStatus ?? 'ENABLED'),
        remark: (body.remark as string | null) ?? null,
      };
      db.seatRules.push(rule);
      db.scopeRules.push({ seatRuleId: rule.seatRuleId, scopes: [] });
      return fulfill(201, ok(rule));
    }

    const adminSeatRuleById = path.match(/^admin\/seat-rules\/(\d+)$/);
    if (method === 'PUT' && adminSeatRuleById) {
      const rule = (db.seatRules as Array<Record<string, unknown>>).find((p) => Number(p.seatRuleId) === Number(adminSeatRuleById[1]));
      if (!rule) return fulfill(404, fail('规则不存在'));
      Object.assign(rule, {
        ruleCode: String(body.ruleCode ?? rule.ruleCode),
        ruleName: String(body.ruleName ?? rule.ruleName),
        ruleType: String(body.ruleType ?? rule.ruleType),
        minSeatCount: body.minSeatCount ?? rule.minSeatCount,
        maxSeatCount: body.maxSeatCount ?? rule.maxSeatCount,
        allowCrossRow: body.allowCrossRow ?? rule.allowCrossRow,
        allowCrossSection: body.allowCrossSection ?? rule.allowCrossSection,
        priority: body.priority ?? rule.priority,
        ruleStatus: String(body.ruleStatus ?? rule.ruleStatus),
        remark: (body.remark as string | null) ?? rule.remark,
      });
      return fulfill(200, ok(rule));
    }

    if (method === 'DELETE' && adminSeatRuleById) {
      const id = Number(adminSeatRuleById[1]);
      db.seatRules = (db.seatRules as Array<Record<string, unknown>>).filter((p) => Number(p.seatRuleId) !== id);
      db.scopeRules = (db.scopeRules as Array<Record<string, unknown>>).filter((s) => Number((s as { seatRuleId: number }).seatRuleId) !== id);
      return fulfill(204, null);
    }

    const adminSeatRuleScopes = path.match(/^admin\/seat-rules\/(\d+)\/scopes$/);
    if (method === 'GET' && adminSeatRuleScopes) {
      const id = Number(adminSeatRuleScopes[1]);
      const entry = (db.scopeRules as Array<Record<string, unknown>>).find((s) => Number((s as { seatRuleId: number }).seatRuleId) === id);
      return fulfill(200, ok((entry?.scopes as unknown[]) ?? []));
    }

    if (method === 'POST' && adminSeatRuleScopes) {
      const id = Number(adminSeatRuleScopes[1]);
      const entry = (db.scopeRules as Array<Record<string, unknown>>).find((s) => Number((s as { seatRuleId: number }).seatRuleId) === id);
      if (!entry) return fulfill(404, fail('规则不存在'));
      const scopes = (entry.scopes as Array<Record<string, unknown>>);
      const scope = {
        ruleScopeId: scopes.length + 1,
        seatRuleId: id,
        scopeType: String(body.scopeType ?? 'MAP'),
        seatMapId: (body.seatMapId as number | null) ?? null,
        seatSectionId: (body.seatSectionId as number | null) ?? null,
        scopeStatus: String(body.scopeStatus ?? 'ENABLED'),
      };
      scopes.push(scope);
      return fulfill(201, ok(scope));
    }

    const adminRuleScopeDelete = path.match(/^admin\/seat-rule-scopes\/(\d+)$/);
    if (method === 'DELETE' && adminRuleScopeDelete) {
      const id = Number(adminRuleScopeDelete[1]);
      db.scopeRules = (db.scopeRules as Array<Record<string, unknown>>).map((entry) => ({
        ...entry,
        scopes: (entry.scopes as Array<Record<string, unknown>>).filter((s) => Number(s.ruleScopeId) !== id),
      }));
      return fulfill(204, null);
    }

    // ---------- 管理端：电子票核销 ----------
    if (method === 'POST' && path === 'admin/tickets/redeem') {
      const now = new Date().toISOString();
      const qrCode = String(body.qrCode ?? '');
      if (!qrCode.trim()) return fulfill(400, fail('请填写电子票二维码内容'));
      return fulfill(200, ok({
        eTicketId: 90001,
        eTicketNo: `ET-REDEEM-${now.replace(/\D/g, '').slice(-8)}`,
        orderId: 1001,
        orderItemId: 100101,
        sessionId: 9001,
        ticketStatus: 'USED',
        checkTime: now,
        checkDevice: String(body.checkDevice ?? 'admin-console'),
        checkBy: 'admin',
      }));
    }

    // ---------- 兜底：未匹配端点返回空成功，避免页面报错 ----------
    return fulfill(200, ok(null));
  });
}