# SHOWTIME 联调问题记录 / FAQ

> 记录开发与联调过程中的常见问题与解决方案，随每次联调同步更新。

---

## 一、数据库与连接

### Q1：启动后端后第一个查库请求报 `Connection string 'Oracle' is not set.`
**不是代码 bug**，是没配置数据库连接串。用 `dotnet user-secrets` 注入本地连接串：

```bash
cd backend
dotnet user-secrets set "ConnectionStrings:Oracle" \
  "User Id=姓名全拼;Password=密码;Data Source=120.27.157.163:1521/XEPDB1"
```

生产环境用环境变量 `ConnectionStrings__Oracle` 注入。

### Q2：个人账号无法建表/改表（DDL 报权限错误）
- Schema 由 `APP_OWNER` 持有，DDL 只能由 `DEPLOY_USER` 执行。
- 个人账号仅能 DML；先用 `ALTER SESSION SET CURRENT_SCHEMA = APP_OWNER;` 切默认 Schema。
- Schema 变更需「讨论 → 写 Alter 脚本入 git → 由 DEPLOY_USER 执行」。

### Q3：Oracle EF Core 查询报 `ORA-00904: "FALSE": invalid identifier`
Oracle 21c 不支持 `TRUE/FALSE` 布尔字面量。EF 的 `AnyAsync` 会被翻译成 `CASE WHEN EXISTS(...) THEN True ELSE False END` 触发该错误。
**解决**：改用手写 `CountAsync(...) > 0`（见 `SeatLockService` 中活动预留判断）。

### Q4：批量插入/查询报 Oracle IN 条件数量超限
Oracle `IN` 列表上限 1000。代码对每订单/每请求座位数统一做了上限（`MaxSeatsPerOrder = 999` 等），批量写入也做了分块（`PersistenceChunkSize = 100`），勿随意调大。

---

## 二、Redis / 选座锁

### Q5：`docker compose up -d redis` 报 `bind ... 6379: address already in use`
本机已有 Redis 在运行，直接用现有的即可，不用重复起容器。

### Q6：Redis 没起/挂了会影响启动和购票吗？
不会。Redis 是**加速层**，`SEAT_LOCK` 表仍是锁的真相源：
- 懒连接 + 自动降级为纯 Oracle 流程，`dotnet test` 不依赖真实 Redis。
- 想彻底关掉 Redis 前置判定，设 `Redis:SeatLockGuardEnabled=false`（kill-switch）。

### Q7：生产排查孤儿 key 能不能用 `KEYS *` / `flushall`？
**不能**。用 `redis-cli --scan --pattern 'showtime:*'` 排查；严禁在生产执行 `KEYS *`、`flushdb`、`flushall`。释放锁必须带 token 比对，禁止按 key 直接 `DEL` 他人锁。

### Q8：选座锁的锁期在哪配？
唯一配置源是 `Redis:SeatLockTtlSeconds`（默认 600 秒），DB 锁期与 Redis key TTL 均取自此值，改锁期只改这一处。

---

## 三、并发与幂等

### Q9：两个用户同时抢同一座位，谁赢？
最终由 Oracle 活动锁唯一索引 `UK_SEAT_LOCK_ACTIVE` 仲裁：插入失败方回滚 Redis 已获取的 key 并返回 409 `SEAT_LOCK_CONFLICT`。Redis 只是前置快速判定，减少无效插入。

### Q10：下单为什么要求 `Idempotency-Key` 请求头？
防止网络重试/双击导致重复下单。服务端按（用户、幂等键、请求摘要）去重：同键同摘要返回原订单，同键不同摘要返回 409 `ORDER_IDEMPOTENCY_CONFLICT`。前端每次下单生成 `crypto.randomUUID()`。

### Q11：改签为什么必须 1:1 选座？
后端契约为「同演出换场次，换几张票选几个目标座位」，每个目标座位携带唯一原票明细 ID（`originalOrderItemId`）。数量不一致或映射重复会被后端拒绝（`EXCHANGE_ITEM_NOT_ELIGIBLE`）。

### Q12：改签选座时提示「目标座位锁定已失效（600 秒）」
锁座 TTL 过期或座位被抢。重新选择目标座位即可（错误码 `EXCHANGE_SEAT_LOCK_INVALID` / `EXCHANGE_TARGET_SEAT_UNAVAILABLE`）。

---

## 四、前端联调

### Q13：前端开箱即用会直连生产后端、操作生产数据？
`frontend/vite.config.ts` 的 `devProxyTarget` 默认指向生产后端。本地联调用环境变量覆盖：

```bash
VITE_DEV_PROXY_TARGET=http://localhost:5146
```

**勿提交该值**。

### Q14：用 `VITE_API_BASE_URL=http://localhost:5146` 直连本地后端被 CORS 拦截
后端未配置 CORS。应走 vite proxy 转发（见 Q13），不要跨域直连。

### Q15：E2E 测试会不会产生生产数据？
不会。`frontend/tests/` 通过 `page.route` 全量 mock `/api/**`，不连接任何真实后端。

### Q16：前端 `.env.development` / `user-secrets` 被误提交？
这些是本地文件。提交前 `git status` 确认没带进 commit；仓库内只保留 `frontend/.env.example` 与 `backend/appsettings.example.json` 占位模板。

---

## 五、消息通知（Outbox / MQ / SignalR）

### Q17：没装 RabbitMQ 会不会影响退款/通知？
不会。默认 `RabbitMq:Enabled=false` 时用进程内 `LocalOrderEventPublisher`，仍走同一 `OrderNotificationMessageHandler → SignalR` 链路，退款批准后不会卡在 `PROCESSING`。RabbitMQ 启用后走 broker + 消费者。

### Q18：消息发布失败怎么办？
Outbox 采用租约 + 指数退避重试（`2^n` 秒，上限 `MaxBackoffSeconds`），达到 `MaxPublishAttempts` 后置终态 `FAILED`（等价 broker DLQ），不会阻塞后续批次。

### Q19：SignalR 推送没收到？
- 确认前端已调用 `ensureRealtimeConnection()` 建立连接。
- 通知是**辅助提示**，订单/退款状态以查询接口为准（页面收到通知后会自动重新拉取列表/详情）。

---

## 六、编码与环境

### Q20：Windows 下中文乱码
编辑器统一 **UTF-8（无 BOM）+ LF**（已由 `.editorconfig` / `.gitattributes` 保证）。cmd 乱码用 `chcp 65001`。

### Q21：JWT 密钥要自己配吗？
本地 `appsettings.Development.json` 内置了 **DEV-ONLY** 密钥，可直接启动。生产**严禁复用**，必须用环境变量 `Jwt__Key` 覆盖。

### Q22：OSS 相关怎么配？
- 本地无 OSS 也能照常开发/跑单测（`Oss:Enabled=false` 时落本地磁盘静态托管）。
- 联调 OSS 时用 `dotnet user-secrets` 注入 `Oss:AccessKeyId` / `Oss:AccessKeySecret`，AccessKey 严禁入库。
- 两者皆关闭时上传接口返回 503 `FILE_STORAGE_NOT_CONFIGURED`。

---

## 附：错误码速查（HTTP 状态）

| HTTP | 语义 | 典型业务错误码 |
|------|------|----------------|
| 400 | 参数错误 | `ORDER_INVALID_ITEMS`、`ORDER_INVALID_IDEMPOTENCY_KEY` |
| 401 | 未认证 | Token 缺失/失效 |
| 403 | 无权限 | 非 Admin 访问 `/api/admin/*` |
| 404 | 资源不存在 | `ORDER_NOT_FOUND`、`SEAT_LOCK_SESSION_NOT_FOUND` |
| 409 | 状态冲突 | `SEAT_LOCK_CONFLICT`、`ORDER_SEAT_LOCK_INVALID`、`ORDER_EXPIRED`、`PAYMENT_ALREADY_SUCCEEDED` |
| 413 | 文件过大 | `FILE_TOO_LARGE` |
| 429 | 限流 | `RATE_LIMIT_EXCEEDED`（含 `Retry-After`） |
| 503 | 存储未配置 | `FILE_STORAGE_NOT_CONFIGURED` |
