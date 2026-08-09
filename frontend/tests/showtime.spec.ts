import { test, expect } from '@playwright/test';

const BASE_URL = "http://127.0.0.1:5173";
const testUser = {
  username: "e2e_test_user01",
  password: "Test@123456"
};

test.describe("Showtime 完整业务E2E测试", () => {

  test.beforeEach(async ({ page }) => {
    await page.goto(BASE_URL);
  });

  test("用户注册新账号", async ({ page }) => {
    // 跳转到注册页
    await page.locator("text=注册").click();

    await page.locator('input[name="username"]').fill(testUser.username);
    await page.locator('input[name="password"]').fill(testUser.password);
    await page.locator('input[name="confirmPassword"]').fill(testUser.password);

    await page.locator("button:has-text('提交注册')").click();

    // 断言注册成功，跳转到登录
    await expect(page).toHaveURL(/login/);
  });

  test("用户正常登录", async ({ page }) => {
    await page.locator("text=登录").click();
    await page.locator('input[name="username"]').fill(testUser.username);
    await page.locator('input[name="password"]').fill(testUser.password);
    await page.locator("button:has-text('登录')").click();

    // 登录成功：页面出现用户名
    await expect(page.locator(`text=${testUser.username}`)).toBeVisible();
  });

  test("浏览演出节目列表", async ({ page }) => {
    // 先登录
    await page.goto(`${BASE_URL}/login`);
    await page.locator('input[name="username"]').fill(testUser.username);
    await page.locator('input[name="password"]').fill(testUser.password);
    await page.locator("button:has-text('登录')").click();

    // 进入演出列表
    await page.locator("text=演出列表").click();
    // 断言节目列表加载出来
    await expect(page.locator(".show-item").first()).toBeVisible();
  });

  test("选择演出座位", async ({ page }) => {
    await page.goto(`${BASE_URL}/login`);
    await page.locator('input[name="username"]').fill(testUser.username);
    await page.locator('input[name="password"]').fill(testUser.password);
    await page.locator("button:has-text('登录')").click();

    // 点开第一个演出详情
    await page.locator(".show-item").first().click();
    // 点击一个可用座位
    await page.locator(".seat.available").first().click();
    // 断言座位被选中
    await expect(page.locator(".seat.selected")).toHaveCount(1);
  });

  test("正常提交订单", async ({ page }) => {
    await page.goto(`${BASE_URL}/login`);
    await page.locator('input[name="username"]').fill(testUser.username);
    await page.locator('input[name="password"]').fill(testUser.password);
    await page.locator("button:has-text('登录')").click();

    await page.locator(".show-item").first().click();
    await page.locator(".seat.available").first().click();
    await page.locator("button:has-text('提交订单')").click();

    // 跳转到订单/支付页面
    await expect(page).toHaveURL(/order/);
  });

  test("订单支付流程", async ({ page }) => {
    await page.goto(`${BASE_URL}/login`);
    await page.locator('input[name="username"]').fill(testUser.username);
    await page.locator('input[name="password"]').fill(testUser.password);
    await page.locator("button:has-text('登录')").click();

    await page.locator(".show-item").first().click();
    await page.locator(".seat.available").first().click();
    await page.locator("button:has-text('提交订单')").click();

    await page.locator("button:has-text('去支付')").click();
    await page.locator("button:has-text('确认付款')").click();

    // 支付成功提示
    await expect(page.locator("text=支付成功")).toBeVisible({ timeout:10000 });
  });

  test("查看我的订单列表", async ({ page }) => {
    await page.goto(`${BASE_URL}/login`);
    await page.locator('input[name="username"]').fill(testUser.username);
    await page.locator('input[name="password"]').fill(testUser.password);
    await page.locator("button:has-text('登录')").click();

    await page.locator("text=我的订单").click();
    await expect(page.locator(".order-item").first()).toBeVisible();
  });

  test("异常：重复下单同一座位", async ({ page }) => {
    await page.goto(`${BASE_URL}/login`);
    await page.locator('input[name="username"]').fill(testUser.username);
    await page.locator('input[name="password"]').fill(testUser.password);
    await page.locator("button:has-text('登录')").click();

    await page.locator(".show-item").first().click();
    // 选已经被占用的座位
    await page.locator(".seat.occupied").click();

    // 期望弹出错误提示
    await expect(page.locator("text=座位已被占用")).toBeVisible();
  });

  test("异常：选择非法座位", async ({ page }) => {
    await page.goto(`${BASE_URL}/login`);
    await page.locator('input[name="username"]').fill(testUser.username);
    await page.locator('input[name="password"]').fill(testUser.password);
    await page.locator("button:has-text('登录')").click();

    await page.locator(".show-item").first().click();
    // JS模拟选择不存在座位，直接调用接口
    const res = await page.request.post("/api/order/create", {
      data:{ showId:1, seatId: 999999 } // 不存在座位ID
    });
    expect(res.status()).toBeGreaterThanOrEqual(400);
  });
});
