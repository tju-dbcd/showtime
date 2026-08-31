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
  // 1️⃣1️⃣ 异常场景：未登录访问用户中心 - 跳过
  // 跟踪：#51 前端登录守卫未实现（https://github.com/tju-dbcd/showtime/issues/51）
  // 实现后再取消 skip 启用负向校验
  // ============================================================
  test.skip('异常：未登录访问用户中心，跳转到登录页', async ({ page }) => {
    await page.context().clearCookies();
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
  // 1️⃣2️⃣ 异常场景：错误密码登录 - 跳过
  // 跟踪：#52 后端密码验证未启用（https://github.com/tju-dbcd/showtime/issues/52）
  // 实现后再取消 skip 启用负向校验
  // ============================================================
  test.skip('异常：错误密码登录失败', async ({ page }) => {
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
});