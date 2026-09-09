# OWASP Top 10 安全测试

## 前置条件

- Python 3 + `requests`
- 后端已启动（默认 `http://localhost:5146`，可用环境变量 `SHOWTIME_BASE_URL` 覆盖）
- 可登录测试账号：先运行 `test/jmeter/seed_users.py` 建号，或用环境变量
  `TEST_ACCOUNT` / `TEST_PASSWORD` 指定已有账号

## 运行方式

```bash
cd test/security
python3 owasp_test.py
```

## 覆盖范围与说明

- A01 失效访问控制：匿名访问受保护接口应 401；普通用户访问 `/api/admin/**` 应 403（垂直越权）
- A03 SQL 注入 / XSS：通过 `/api/client/shows?keyword=` 做反射冒烟（后端为参数化查询，黑盒仅能冒烟）
- A05 敏感信息泄露：错误/404 响应不得携带堆栈
- A07 爆破与限流：连续错误密码必须触发 login 429 限流（5 次/分钟/IP）
- CSRF：本项目为纯 Bearer JWT、无 Cookie 会话，CSRF 攻击面**不适用**，故不执行
- 水平越权：订单/锁座/实名等资源均由 JWT sub 归属并按当前用户过滤，黑盒无法构造跨用户请求

## 退出码

- `0`：全部用例通过
- `1`：存在失败用例
- `2`：前置条件失败（如测试账号无法登录）

> 脚本末段会触发 login 限流，重跑前建议等待约 1 分钟。
