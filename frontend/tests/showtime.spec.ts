import { test, expect, type Page } from '@playwright/test';
import { mockApi } from './mocks/api';

const BASE_URL = 'http://127.0.0.1:5173';

interface TestUser {
  username: string;
  phone: string;
  password: string;
  email: string;
}

function generateTestUser(): TestUser {
  const timestamp = Date.now() + Math.floor(Math.random() * 10000);
  return {
    username: `e2e_test_${timestamp}`,
    phone: `1${String(Math.floor(Math.random() * 1000000000)).padStart(9, '0')}`,
    password: 'Test1234', // 满足注册规则：至少 8 位且含字母和数字
    email: `e2e_${timestamp}@example.com`,
  };
}

/**
 * 注册辅助函数：按前端表单真实 placeholder 精确匹配。
 * 注册成功后后端返回 success=true，前端 setTimeout(500) 后跳转 /login。
 */
async function registerUser(page: Page, user: TestUser): Promise<void> {
  await page.goto(`${BASE_URL}/register`);
  await page.locator('input[placeholder="用户名（3-50位，字母开头）"]').fill(user.username);
  await page.locator('input[placeholder="手机号"]').fill(user.phone);
  await page.locator('input[placeholder="邮箱（选填）"]').fill(user.email);
  await page.locator('input[placeholder="密码（至少8位，含字母和数字）"]').fill(user.password);
  await page.locator('input[placeholder="确认密码"]').fill(user.password);
  await page.locator('button:has-text("注 册")').click();
  await page.waitForURL(/\/login$/, { timeout: 15000 });
  await expect(page).toHaveURL(/\/login$/);
}

/** 登录辅助函数：登录后前端跳转首页 "/"。 */
async function loginUser(page: Page, user: TestUser): Promise<void> {
  await page.goto(`${BASE_URL}/login`);
  await page.locator('input[placeholder="用户名"]').fill(user.username);
  await page.locator('input[placeholder="密码"]').fill(user.password);
  await page.locator('button:has-text("登 录")').click();
  await page.waitForURL(/\/$/, { timeout: 15000 });
  await expect(page).toHaveURL(/\/$/);
}

/** 从首页一路点进选座页：/search → 演出详情 → 选座 */
async function gotoSeatSelection(page: Page): Promise<void> {
  await page.goto(`${BASE_URL}/search`);
  await page.waitForLoadState('networkidle');
  await page.locator('.ant-card').first().waitFor({ state: 'visible', timeout: 15000 });
  await page.locator('.ant-card').first().click();
  await page.waitForLoadState('networkidle');
  await page.locator('.buy-btn:has-text("立即抢票")').waitFor({ state: 'visible', timeout: 10000 });
  await page.locator('.buy-btn:has-text("立即抢票")').click();
  await page.waitForLoadState('networkidle');
}

/** 在选座页选第一个可用座位并确认下单，最终落在 /order */
async function pickSeatAndOrder(page: Page): Promise<void> {
  await page.locator('.seat.available').first().waitFor({ state: 'visible', timeout: 15000 });
  await page.locator('.seat.available').first().click();
  await expect(page.locator('.seat.selected')).toHaveCount(1);
  await page.locator('button:has-text("确认选座")').click();
  await page.waitForURL(/\/order$/, { timeout: 15000 });
  await expect(page).toHaveURL(/\/order$/);
}

/** 在选座页选两个可用座位并确认下单，最终落在 /order */
async function pickTwoSeatsAndOrder(page: Page): Promise<void> {
  await page.locator('.seat.available').first().waitFor({ state: 'visible', timeout: 15000 });
  await page.locator('.seat.available').nth(0).click();
  await page.locator('.seat.available').nth(1).click();
  await expect(page.locator('.seat.selected')).toHaveCount(2);
  await page.locator('button:has-text("确认选座")').click();
  await page.waitForURL(/\/order$/, { timeout: 15000 });
  await expect(page).toHaveURL(/\/order$/);
}

/** 支付当前第一笔待支付订单（微信 mock 支付），订单进入已支付 */
async function payFirstOrder(page: Page): Promise<void> {
  const payBtn = page.locator('.pay-btn:has-text("立即付款")');
  await payBtn.waitFor({ state: 'visible', timeout: 15000 });
  await payBtn.click();
  await page.locator('.payment-modal').waitFor({ state: 'visible', timeout: 10000 });
  await page.locator('button:has-text("微信支付")').click();
  await expect(page.getByText('支付成功')).toBeVisible({ timeout: 15000 });
  await expect(page.locator('.ant-tag:has-text("已支付")').first()).toBeVisible({ timeout: 15000 });
}

/** 从订单列表进入第一笔已支付订单的详情页 */
async function gotoFirstOrderDetail(page: Page): Promise<void> {
  await page.locator('button:has-text("查看详情")').first().waitFor({ state: 'visible', timeout: 15000 });
  await page.locator('button:has-text("查看详情")').first().click();
  await page.waitForURL(/\/order\/\d+$/, { timeout: 15000 });
}

test.describe.serial('Showtime 完整业务E2E测试集', () => {
  // 全部 /api 请求走 mock，不触达任何真实后端（本地/生产均不连接）
  test.beforeEach(async ({ page }) => {
    await mockApi(page);
  });

  // ============================================================
  // 1️⃣ 用户注册
  // ============================================================
  test('用户注册新账号', async ({ page }) => {
    const testUser = generateTestUser();

    await page.goto(`${BASE_URL}/register`);
    await page.waitForLoadState('networkidle');

    await page.locator('input[placeholder="用户名（3-50位，字母开头）"]').fill(testUser.username);
    await page.locator('input[placeholder="手机号"]').fill(testUser.phone);
    await page.locator('input[placeholder="邮箱（选填）"]').fill(testUser.email);
    await page.locator('input[placeholder="密码（至少8位，含字母和数字）"]').fill(testUser.password);
    await page.locator('input[placeholder="确认密码"]').fill(testUser.password);

    await page.locator('button:has-text("注 册")').click();
    await page.waitForURL(/\/login$/, { timeout: 15000 });
    await expect(page).toHaveURL(/\/login$/);
  });

  // ============================================================
  // 2️⃣ 用户登录
  // ============================================================
  test('注册账号正常登录', async ({ page }) => {
    const testUser = generateTestUser();

    await registerUser(page, testUser);
    await loginUser(page, testUser);
    await expect(page).toHaveURL(/\/$/);
  });

  // ============================================================
  // 3️⃣ 浏览演出列表
  // ============================================================
  test('浏览演出列表', async ({ page }) => {
    const testUser = generateTestUser();

    await registerUser(page, testUser);
    await loginUser(page, testUser);

    await page.goto(`${BASE_URL}/search`);
    await page.waitForLoadState('networkidle');
    await expect(page.locator('.ant-card').first()).toBeVisible({ timeout: 15000 });
  });

  // ============================================================
  // 4️⃣ 搜索演出
  // ============================================================
  test('搜索演出', async ({ page }) => {
    const testUser = generateTestUser();

    await registerUser(page, testUser);
    await loginUser(page, testUser);

    await page.goto(`${BASE_URL}/search`);
    await page.waitForLoadState('networkidle');

    const searchInput = page.locator('input[placeholder="搜索演出..."]');
    await searchInput.waitFor({ state: 'visible', timeout: 10000 });
    await searchInput.fill('演唱会');
    await searchInput.press('Enter');

    await page.waitForTimeout(1000);
    await expect(page.locator('.ant-card').first()).toBeVisible({ timeout: 10000 });
  });

  // ============================================================
  // 5️⃣ 查看演出详情
  // ============================================================
  test('查看演出详情', async ({ page }) => {
    test.setTimeout(60000);
    const testUser = generateTestUser();

    await registerUser(page, testUser);
    await loginUser(page, testUser);

    await page.goto(`${BASE_URL}/search`);
    await page.waitForLoadState('networkidle');
    await page.locator('.ant-card').first().click();
    await page.waitForLoadState('networkidle');

    await expect(page.locator('.detail-title')).toBeVisible({ timeout: 10000 });
    await expect(page.locator('.buy-btn:has-text("立即抢票")')).toBeVisible({ timeout: 10000 });
  });

  // ============================================================
  // 6️⃣ 选择座位
  // ============================================================
  test('选择座位', async ({ page }) => {
    test.setTimeout(60000);
    const testUser = generateTestUser();

    await registerUser(page, testUser);
    await loginUser(page, testUser);

    await gotoSeatSelection(page);
    await pickSeatAndOrder(page);
    await expect(page.locator('.ant-tag:has-text("待支付")').first()).toBeVisible({ timeout: 15000 });
  });

  // ============================================================
  // 7️⃣ 完整购票流程（强断言：支付链路每一步必须真实发生）
  // ============================================================
  test('完整购票流程：选座、下单、支付', async ({ page }) => {
    test.setTimeout(60000);
    const testUser = generateTestUser();

    await registerUser(page, testUser);
    await loginUser(page, testUser);

    await gotoSeatSelection(page);
    await pickSeatAndOrder(page);

    // 1. 订单列表必须出现「待支付」状态（弱化为 if 即为静默放行，此处强断言）
    await expect(page.locator('.ant-tag:has-text("待支付")').first()).toBeVisible({ timeout: 15000 });

    // 2. 点击「立即付款」打开支付弹窗
    const payBtn = page.locator('.pay-btn:has-text("立即付款")');
    await payBtn.waitFor({ state: 'visible', timeout: 10000 });
    await payBtn.click();

    const modal = page.locator('.payment-modal');
    await modal.waitFor({ state: 'visible', timeout: 10000 });

    // 3. 选择微信支付（模拟支付接口），必须出现「支付成功」提示
    await page.locator('button:has-text("微信支付")').click();
    await expect(page.getByText('支付成功')).toBeVisible({ timeout: 15000 });

    // 4. 弹窗关闭后订单列表刷新，订单进入「已支付」
    await expect(page.locator('.ant-tag:has-text("已支付")').first()).toBeVisible({ timeout: 15000 });
  });

  // ============================================================
  // 8️⃣ 查看我的订单（全新用户 -> 空态）
  // ============================================================
  test('查看我的订单（新用户空态）', async ({ page }) => {
    const testUser = generateTestUser();

    await registerUser(page, testUser);
    await loginUser(page, testUser);

    await page.goto(`${BASE_URL}/order`);

    // 新用户没有任何订单：断言空态（antd Empty）而非订单表格首行
    await expect(page.locator('.ant-empty')).toBeVisible({ timeout: 15000 });
  });

  // ============================================================
  // 9️⃣ 异常场景：下单后未支付，订单保持待支付（自动取消为分钟级，UI 不做长等待）
  // ============================================================
  test('异常：下单后未支付订单保持待支付', async ({ page }) => {
    test.setTimeout(60000);
    const testUser = generateTestUser();

    await registerUser(page, testUser);
    await loginUser(page, testUser);

    await gotoSeatSelection(page);
    await pickSeatAndOrder(page);
    await expect(page.locator('.ant-tag:has-text("待支付")').first()).toBeVisible({ timeout: 15000 });

    // 后端订单过期时间为 15 分钟（OrderService.AddMinutes(15)），自动取消不在 UI 等待范围；
    // 这里用「短暂等待 + 刷新后仍为待支付」验证不会异常提前取消
    await page.waitForTimeout(5000);
    await page.reload();
    await expect(page.locator('.ant-tag:has-text("待支付")').first()).toBeVisible({ timeout: 15000 });
  });

  // ============================================================
  // 🔟 异常场景：同一座位重复下单（座位置为已售）
  // ============================================================
  test('异常：已下单座位二次进入变为已售', async ({ page }) => {
    test.setTimeout(60000);
    const testUser = generateTestUser();

    await registerUser(page, testUser);
    await loginUser(page, testUser);

    await gotoSeatSelection(page);
    await pickSeatAndOrder(page);

    // 重新直达选座页（强制重新拉取座位图）：已下单座位应显示为已售/不可用
    await page.goto(`${BASE_URL}/seat-selection/1`);
    await expect(page.locator('.seat.sold, .seat.unavailable').first()).toBeVisible({ timeout: 15000 });
  });

  // ============================================================
  // 1️⃣1️⃣ 异常场景：未登录访问用户中心，跳转到登录页
  // 依赖 UserCenter 组件级守卫：无 accessToken 时 useEffect 中 navigate('/login')
  // ============================================================
  test('异常：未登录访问用户中心，跳转到登录页', async ({ page }) => {
    // 每条用例是全新 context，localStorage 天然为空（登录态存 localStorage 而非 cookie）
    await page.goto(`${BASE_URL}/usercenter`);
    await page.waitForLoadState('networkidle');
    await page.waitForTimeout(2000);

    const currentUrl = page.url();

    if (currentUrl.includes('/login')) {
      await expect(page).toHaveURL(/\/login/);
    } else {
      const loginDialog = page.locator('.ant-modal:has-text("登录"), .ant-modal:has-text("请登录")');
      if (await loginDialog.count() > 0) {
        await expect(loginDialog).toBeVisible();
      } else {
        throw new Error('未登录访问用户中心：页面没有登录守卫，请实现认证拦截（见 issue #51）');
      }
    }
  });

  // ============================================================
  // 1️⃣2️⃣ 异常场景：错误密码登录失败
  // 后端 AuthService 用 IPasswordHasher 哈希校验，错误密码返回 401，前端停留在 /login
  // ============================================================
  test('异常：错误密码登录失败', async ({ page }) => {
    const testUser = generateTestUser();

    await registerUser(page, testUser);

    await page.goto(`${BASE_URL}/login`);
    await page.waitForLoadState('networkidle');

    await page.locator('input[placeholder="用户名"]').fill(testUser.username);
    await page.locator('input[placeholder="密码"]').fill('wrongpassword123');

    const loginBtn = page.locator('button:has-text("登 录")');
    await loginBtn.waitFor({ state: 'visible', timeout: 10000 });
    await loginBtn.click();

    await page.waitForTimeout(2000);

    const currentUrl = page.url();

    if (currentUrl.includes('/login')) {
      await expect(page).toHaveURL(/\/login/);
    } else {
      throw new Error('错误密码登录成功：后端密码验证未启用，请修复认证逻辑（见 issue #52）');
    }
  });

  // ============================================================
  // 1️⃣3️⃣ 退票提交流程：已支付订单 → 申请退票 → 报价 → 提交待审核
  // ============================================================
  test('退票提交（报价→填写原因→提交进入待审核）', async ({ page }) => {
    test.setTimeout(90000);
    const testUser = generateTestUser();

    await registerUser(page, testUser);
    await loginUser(page, testUser);

    await gotoSeatSelection(page);
    await pickSeatAndOrder(page);
    await payFirstOrder(page);
    await gotoFirstOrderDetail(page);

    // 1. 打开申请退票弹窗，展示报价（可退金额）
    await page.locator('button:has-text("申请退票")').waitFor({ state: 'visible', timeout: 15000 });
    await page.locator('button:has-text("申请退票")').click();
    await expect(page.getByText('可退金额')).toBeVisible({ timeout: 15000 });

    // 2. 填写原因并提交，进入待审核
    await page.locator('textarea[placeholder="请填写退票原因（必填）"]').fill('行程冲突，无法观演');
    await page.locator('button:has-text("提交退票申请")').click();
    await expect(page.getByText('退票申请已提交，请等待审核')).toBeVisible({ timeout: 15000 });
  });

  // ============================================================
  // 1️⃣4️⃣ 改签多票 1:1 映射：双票订单 → 改签选 2 座 → 正确一一对应 → 自动报价 → 提交待审核
  // 回归守卫：若前端把两个目标座位都兜底映射到同一张原票（originalOrderItemId 重复）
  // 或数量不匹配（≠2），mock 会按后端契约拒绝并返回 EXCHANGE_ITEM_NOT_ELIGIBLE，
  // 报价/提交将失败，该用例随即报错。
  // ============================================================
  test('改签多票 1:1 映射（2 张原票 → 2 个目标座位 → 自动报价 → 提交待审核）', async ({ page }) => {
    test.setTimeout(120000);
    const testUser = generateTestUser();

    await registerUser(page, testUser);
    await loginUser(page, testUser);

    await gotoSeatSelection(page);
    await pickTwoSeatsAndOrder(page);
    await payFirstOrder(page);
    await gotoFirstOrderDetail(page);

    // 1. 打开申请改签弹窗，目标演出固定为原演出，且仅列出其他在售场次
    await page.locator('button:has-text("申请改签")').waitFor({ state: 'visible', timeout: 15000 });
    await page.locator('button:has-text("申请改签")').click();
    await expect(page.getByText('选择目标场次', { exact: true })).toBeVisible({ timeout: 15000 });

    // 2. 选择目标场次（原场次 9001 被排除，仅剩 9003）
    const sessionSelect = page.locator('.ant-modal .ant-select').last();
    await sessionSelect.click();
    await page.locator('.ant-select-dropdown .ant-select-item-option').first().click();

    // 3. 跳转选座页，改签模式需选 2 个目标座位（与原票一一对应）
    await expect(page.getByText('需选择 2 个座位（与原票一一对应）')).toBeVisible({ timeout: 15000 });
    await page.locator('button:has-text("点击选择目标座位")').click();
    await page.waitForURL(/\/seat-selection\/\d+$/, { timeout: 15000 });
    await expect(page.getByText('（改签模式）')).toBeVisible({ timeout: 15000 });

    await page.locator('.seat.available').first().waitFor({ state: 'visible', timeout: 15000 });
    await page.locator('.seat.available').nth(0).click();
    await page.locator('.seat.available').nth(1).click();
    await expect(page.locator('.seat.selected')).toHaveCount(2);
    await page.locator('button:has-text("确认改签座位")').click();
    await page.waitForURL(/\/order\/\d+$/, { timeout: 15000 });

    // 4. 返回订单详情：弹窗自动打开并自动报价（1:1 映射通过 mock 契约校验）
    await expect(page.getByText('需补差价')).toBeVisible({ timeout: 20000 });
    await expect(page.getByText('获取改签报价')).not.toBeVisible();

    // 5. 提交改签申请，进入待审核状态面板
    await page.locator('button:has-text("提交改签申请")').click();
    await expect(page.getByText('改签申请已提交，请等待审核')).toBeVisible({ timeout: 15000 });
    await expect(page.getByText('改签申请', { exact: true })).toBeVisible({ timeout: 15000 });
    await expect(page.getByText('待审核', { exact: true }).first()).toBeVisible({ timeout: 15000 });
  });
});