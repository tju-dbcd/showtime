/**
 * 管理端 E2E：全部 /api 请求走 mock（tests/mocks/api.ts），不触达真实后端。
 * 覆盖本次前端管理端缺口补全的新增页面与接线：
 *  - 退票审核（通过/驳回）
 *  - 改签审核（详情/通过）
 *  - 退票策略 / 改签策略（新建）
 *  - 座位规则（新建 + 作用域）
 *  - 场次动态定价（整批保存）
 *  - 订单补出票 + 关联退票单
 *  - 电子票核销
 */
import { test, expect, type Page } from '@playwright/test';
import { mockApi, type MockDb } from './mocks/api';

const BASE_URL = 'http://127.0.0.1:5173';

/** 管理员登录（mock 内置 admin/admin123，角色 Admin） */
async function loginAdmin(page: Page): Promise<void> {
  await page.goto(`${BASE_URL}/login`);
  await page.waitForLoadState('networkidle');
  await page.locator('input[placeholder="用户名"]').fill('admin');
  await page.locator('input[placeholder="密码"]').fill('admin123');
  await page.locator('button:has-text("登 录")').click();
  await page.waitForURL(/\/$/, { timeout: 15000 });
  await expect(page).toHaveURL(/\/$/);
}

function seedRefund(db: MockDb, id: number, refundNo: string, opts: Partial<Record<string, unknown>> = {}): void {
  db.refunds.push({
    refundId: id,
    refundNo,
    orderId: 1001,
    userId: 1,
    refundType: 'FULL',
    refundAmount: 100,
    feeRate: 0.2,
    appliedServiceFee: 0,
    actualRefund: 80,
    approveStatus: 'PENDING',
    refundStatus: 'PENDING',
    reviewBy: null,
    reviewTime: null,
    reviewRemark: null,
    completeTime: null,
    createTime: '2026-01-01T10:00:00',
    ...opts,
  });
}

function seedExchange(db: MockDb, id: number, exchangeNo: string, opts: Partial<Record<string, unknown>> = {}): void {
  db.exchanges.push({
    exchangeId: id,
    exchangeNo,
    originalOrderId: 1001,
    childOrderId: 1001001,
    userId: 1,
    origSessionId: 9001,
    targetSessionId: 9002,
    reason: '更想要其他场次',
    origDeduction: 200,
    targetAmount: 220,
    priceDiff: 20,
    exchangeFee: 20,
    amountDue: 40,
    appliedPolicyId: 1,
    policyName: '默认政策',
    approveStatus: 'PENDING',
    exchangeStatus: 'PENDING',
    reviewBy: null,
    reviewTime: null,
    reviewRemark: null,
    completeTime: null,
    expireTime: '2026-01-02T10:00:00',
    createTime: '2026-01-01T10:00:00',
    items: [],
    ...opts,
  });
}

function seedPaidOrder(db: MockDb): void {
  db.orders.push({
    orderId: 1001,
    orderNo: 'SO-PAID-1001',
    sessionId: 9001,
    totalAmount: 200,
    discountAmount: 0,
    ticketCount: 2,
    orderStatus: 'PAID',
    expireTime: '2026-01-10T10:00:00',
    payTime: '2026-01-01T10:05:00',
    issueTime: null,
    cancelTime: null,
    source: 'WEB',
    remark: null,
    createTime: '2026-01-01T10:00:00',
    items: [
      { orderItemId: 100101, seatId: 1, priceStrategyId: 1, unitPrice: 100, itemStatus: 'NORMAL' },
      { orderItemId: 100102, seatId: 2, priceStrategyId: 1, unitPrice: 100, itemStatus: 'NORMAL' },
    ],
    payments: [],
    tickets: [],
  });
}

/** 通用：点击中文按钮。antd 会对「2 个汉字」的主按钮自动插空格（保 存），但 link 按钮不插（通过）。用正则同时兼容两种渲染。 */
function buttonWithChinese(page: Page, text: string): ReturnType<Page['locator']> {
  const pattern = text.length === 2 ? new RegExp(text.split('').join('\\s*')) : new RegExp(text);
  return page.getByRole('button', { name: pattern });
}

test.describe.serial('管理端 E2E：缺口补全功能', () => {
  test.beforeEach(async ({ page }) => {
    await mockApi(page);
  });

  // ============================================================
  // 1️⃣ 管理端登录可进入后台
  // ============================================================
  test('管理员登录并进入后台', async ({ page }) => {
    await loginAdmin(page);
    await page.goto(`${BASE_URL}/admin`);
    await page.waitForLoadState('networkidle');
    await expect(page.getByText('管理后台')).toBeVisible({ timeout: 15000 });
    await expect(page.locator('.ant-menu').getByText('退票审核')).toBeVisible();
    await expect(page.locator('.ant-menu').getByText('电子票核销')).toBeVisible();
  });

  // ============================================================
  // 2️⃣ 退票审核：通过
  // ============================================================
  test('退票审核：通过申请', async ({ page }) => {
    await mockApi(page, (db) => {
      seedRefund(db, 11, 'RF-E2E-APPROVE');
    });
    await loginAdmin(page);

    await page.goto(`${BASE_URL}/admin/refund`);
    const row = page.locator('tbody tr').filter({ hasText: 'RF-E2E-APPROVE' });
    await expect(row).toBeVisible({ timeout: 15000 });
    await expect(row.locator('.ant-tag:has-text("待审核")')).toBeVisible();

    await row.locator(buttonWithChinese(page, '通过')).click();
    await expect(page.locator('.ant-modal:has-text("通过退票申请")')).toBeVisible();
    await page.locator('button:has-text("确认通过")').click();

    await expect(page.getByText('退票申请已通过')).toBeVisible({ timeout: 15000 });
    await expect(row.locator('.ant-tag:has-text("已通过")')).toBeVisible({ timeout: 15000 });
  });

  // ============================================================
  // 3️⃣ 退票审核：驳回必须填备注
  // ============================================================
  test('退票审核：驳回必须填写备注', async ({ page }) => {
    await mockApi(page, (db) => {
      seedRefund(db, 12, 'RF-E2E-REJECT');
    });
    await loginAdmin(page);

    await page.goto(`${BASE_URL}/admin/refund`);
    const row = page.locator('tbody tr').filter({ hasText: 'RF-E2E-REJECT' });
    await expect(row).toBeVisible({ timeout: 15000 });

    await row.locator(buttonWithChinese(page, '驳回')).click();
    await page.locator('button:has-text("确认驳回")').click();

    // 未填备注时拦截
    await expect(page.getByText('驳回时必须填写备注')).toBeVisible();
    await expect(page.locator('.ant-modal:has-text("驳回退票申请")')).toBeVisible();

    await page.locator('.ant-modal textarea').fill('用户信息与实名不一致');
    await page.locator('button:has-text("确认驳回")').click();

    await expect(page.getByText('退票申请已驳回')).toBeVisible({ timeout: 15000 });
    await expect(row.locator('.ant-tag:has-text("已驳回")')).toBeVisible({ timeout: 15000 });
  });

  // ============================================================
  // 4️⃣ 改签审核：详情 + 通过
  // ============================================================
  test('改签审核：查看详情并通过', async ({ page }) => {
    await mockApi(page, (db) => {
      seedExchange(db, 21, 'EX-E2E-APPROVE');
    });
    await loginAdmin(page);

    await page.goto(`${BASE_URL}/admin/exchange`);
    const row = page.locator('tbody tr').filter({ hasText: 'EX-E2E-APPROVE' });
    await expect(row).toBeVisible({ timeout: 15000 });

    await row.locator(buttonWithChinese(page, '详情')).click();
    const detail = page.locator('.ant-modal:has-text("改签申请详情")');
    await expect(detail).toBeVisible({ timeout: 15000 });
    await expect(detail.getByText('EX-E2E-APPROVE')).toBeVisible();

    await detail.locator(buttonWithChinese(page, '通过')).click();
    await page.locator('button:has-text("确认通过")').click();

    await expect(page.getByText('改签申请已通过')).toBeVisible({ timeout: 15000 });
    // 详情刷新后审核状态变为已通过，操作按钮消失
    await expect(detail.getByText('已通过').first()).toBeVisible({ timeout: 15000 });
    await expect(detail.locator(buttonWithChinese(page, '通过'))).toHaveCount(0);
  });

  // ============================================================
  // 5️⃣ 退票策略：新建
  // ============================================================
  test('退票策略：新建策略', async ({ page }) => {
    await loginAdmin(page);

    await page.goto(`${BASE_URL}/admin/refund-policy`);
    await page.locator('button:has-text("新建策略")').click();
    await expect(page.locator('.ant-modal:has-text("新建退票策略")')).toBeVisible();

    await page.locator('.ant-modal input#policyName').fill('E2E开场前2小时退80%');
    await page.locator('.ant-modal input#refundDeadlineHour').fill('2');
    await page.locator('.ant-modal input#refundRate').fill('0.8');
    await page.locator('.ant-modal input#priority').fill('0');
    await buttonWithChinese(page, '保存').click();

    await expect(page.getByText('策略已创建')).toBeVisible({ timeout: 15000 });
    await expect(page.locator('tbody tr').filter({ hasText: 'E2E开场前2小时退80%' })).toBeVisible({ timeout: 15000 });
    await expect(page.locator('tbody tr').filter({ hasText: 'E2E开场前2小时退80%' }).locator('.ant-tag:has-text("启用")')).toBeVisible();
  });

  // ============================================================
  // 6️⃣ 改签策略：新建
  // ============================================================
  test('改签策略：新建策略', async ({ page }) => {
    await loginAdmin(page);

    await page.goto(`${BASE_URL}/admin/exchange-policy`);
    await page.locator('button:has-text("新建策略")').click();
    await expect(page.locator('.ant-modal:has-text("新建改签策略")')).toBeVisible();

    await page.locator('.ant-modal input#policyName').fill('E2E开场前3小时可改签');
    await page.locator('.ant-modal input#exchangeDeadlineHour').fill('3');
    await page.locator('.ant-modal input#exchangeFee').fill('20');
    await page.locator('.ant-modal input#priority').fill('0');
    await buttonWithChinese(page, '保存').click();

    await expect(page.getByText('策略已创建')).toBeVisible({ timeout: 15000 });
    await expect(page.locator('tbody tr').filter({ hasText: 'E2E开场前3小时可改签' })).toBeVisible({ timeout: 15000 });
    await expect(page.locator('tbody tr').filter({ hasText: 'E2E开场前3小时可改签' }).getByText('不允许')).toBeVisible();
  });

  // ============================================================
  // 7️⃣ 座位规则：新建规则 + 添加作用域
  // ============================================================
  test('座位规则：新建规则并绑定座位图作用域', async ({ page }) => {
    await loginAdmin(page);

    await page.goto(`${BASE_URL}/admin/seat-rule`);
    await page.locator('button:has-text("新建规则")').click();
    await expect(page.locator('.ant-modal:has-text("新建座位规则")')).toBeVisible();

    await page.locator('.ant-modal input#ruleCode').fill('E2E_LIMIT');
    await page.locator('.ant-modal input#ruleName').fill('E2E限购2张');
    // 规则类型选择「限购数量」
    await page.locator('.ant-modal #ruleType').click();
    await page.locator('.ant-select-item-option', { hasText: '限购数量' }).click();
    await page.locator('.ant-modal input#minSeatCount').fill('1');
    await page.locator('.ant-modal input#maxSeatCount').fill('2');
    await buttonWithChinese(page, '保存').click();

    await expect(page.getByText('规则已创建')).toBeVisible({ timeout: 15000 });
    const row = page.locator('tbody tr').filter({ hasText: 'E2E限购2张' });
    await expect(row).toBeVisible({ timeout: 15000 });

    // 打开作用域抽屉并添加座位图作用域
    await row.locator('button:has-text("作用域")').click();
    await expect(page.locator('.ant-drawer:has-text("作用域")')).toBeVisible();
    await page.locator('button:has-text("添加作用域")').click();
    await expect(page.locator('.ant-modal:has-text("添加作用域")')).toBeVisible();

    await page.locator('.ant-modal #seatMapId').click();
    await page.locator('.ant-select-item-option', { hasText: '主会场座位图' }).first().click();
    await page.locator('.ant-modal:has-text("添加作用域")').getByRole('button', { name: /添\s*加/ }).click();

    await expect(page.getByText('作用域已添加')).toBeVisible({ timeout: 15000 });
    await expect(page.locator('.ant-drawer tbody tr').filter({ hasText: '座位图#5001' })).toBeVisible({ timeout: 15000 });
  });

  // ============================================================
  // 8️⃣ 场次动态定价：整批保存
  // ============================================================
  test('场次管理：配置动态定价规则并保存', async ({ page }) => {
    await loginAdmin(page);

    await page.goto(`${BASE_URL}/admin/session`);
    // 选择演出（触发场次列表加载）
    await page.locator('.ant-select', { hasText: '请选择演出' }).click();
    await page.locator('.ant-select-item-option', { hasText: '「星光」演唱会之夜' }).click();
    const sessionRow = page.locator('tbody tr').filter({ hasText: '9001' });
    await expect(sessionRow).toBeVisible({ timeout: 15000 });

    await sessionRow.locator('button:has-text("动态定价")').click();
    const modal = page.locator('.ant-modal:has-text("动态定价规则")');
    await expect(modal).toBeVisible({ timeout: 15000 });

    // 添加一条规则
    await modal.locator('button:has-text("添加规则")').click();
    await modal.locator('input[placeholder="规则名"]').fill('开演前2小时折扣');
    await modal.locator('.ant-select', { hasText: '触发类型' }).click();
    await page.locator('.ant-select-item-option', { hasText: '时间窗口' }).click();
    await modal.locator('input[placeholder="起始偏移(分)"]').fill('120');
    await modal.locator('input[placeholder="结束偏移(分)"]').fill('30');
    await modal.locator('.ant-select', { hasText: '调整方式' }).click();
    await page.locator('.ant-select-item-option', { hasText: '折扣率' }).click();
    await modal.locator('input[placeholder="调整值"]').fill('0.9');

    const saveResponse = page.waitForResponse(
      (resp) => resp.url().includes('/api/admin/sessions/9001/dynamic-pricing-rules') && resp.request().method() === 'POST',
      { timeout: 15000 },
    );
    await modal.locator('button:has-text("保存（整批覆盖）")').click();
    const response = await saveResponse;
    expect(response.status()).toBe(200);

    await expect(page.getByText('动态定价规则已保存（整批覆盖）')).toBeVisible({ timeout: 15000 });
    await expect(modal).toBeHidden();
  });

  // ============================================================
  // 9️⃣ 订单管理：补出票
  // ============================================================
  test('订单管理：为已支付订单补出票', async ({ page }) => {
    await mockApi(page, (db) => {
      seedPaidOrder(db);
    });
    await loginAdmin(page);

    await page.goto(`${BASE_URL}/admin/order`);
    const row = page.locator('tbody tr').filter({ hasText: 'SO-PAID-1001' });
    await expect(row).toBeVisible({ timeout: 15000 });
    await expect(row.locator('.ant-tag:has-text("已支付")')).toBeVisible();

    await row.locator('button:has-text("补出票")').click();
    await expect(page.locator('.ant-popconfirm:has-text("补出票")')).toBeVisible();
    await buttonWithChinese(page, '确定').click();

    await expect(page.getByText('补出票成功')).toBeVisible({ timeout: 15000 });
    await expect(row.locator('.ant-tag:has-text("已出票")')).toBeVisible({ timeout: 15000 });
  });

  // ============================================================
  // 🔟 订单管理：详情展示关联退票单
  // ============================================================
  test('订单管理：详情展示关联退票单并可跳转', async ({ page }) => {
    await mockApi(page, (db) => {
      seedPaidOrder(db);
      seedRefund(db, 13, 'RF-E2E-RELATED');
    });
    await loginAdmin(page);

    await page.goto(`${BASE_URL}/admin/order`);
    const row = page.locator('tbody tr').filter({ hasText: 'SO-PAID-1001' });
    await expect(row).toBeVisible({ timeout: 15000 });

    await row.locator(buttonWithChinese(page, '详情')).click();
    const detail = page.locator('.ant-modal:has-text("订单详情")');
    await expect(detail).toBeVisible({ timeout: 15000 });
    await expect(detail.getByText('关联售后单')).toBeVisible({ timeout: 15000 });
    await expect(detail.locator('tbody tr').filter({ hasText: 'RF-E2E-RELATED' })).toBeVisible();
    await expect(detail.getByText('去审核/查看').first()).toBeVisible();
  });

  // ============================================================
  // 1️⃣1️⃣ 电子票核销
  // ============================================================
  test('电子票核销：成功显示核销结果', async ({ page }) => {
    await loginAdmin(page);

    await page.goto(`${BASE_URL}/admin/redeem`);
    await page.locator('input[placeholder="扫码或粘贴电子票完整二维码内容"]').fill('MOCK-QR-1001');
    await page.locator('input[placeholder="如：闸机01 / 检票口A"]').fill('闸机01');
    await buttonWithChinese(page, '核销').click();

    await expect(page.getByText('核销成功', { exact: false }).first()).toBeVisible({ timeout: 15000 });
    await expect(page.locator('.ant-card:has-text("核销结果")')).toBeVisible({ timeout: 15000 });
    await expect(page.locator('.ant-card:has-text("核销结果")').getByText('闸机01')).toBeVisible();
    await expect(page.locator('.ant-card:has-text("核销结果")').locator('.ant-tag:has-text("已核销")')).toBeVisible();
  });
});
