# SHOWTIME 接口变更日志

> 本文件记录数据库 Schema 变更与对应接口/功能演进，随每次 API 新增同步更新。
> 变更脚本位于 `db/migrations/`（命名 `日期__模块描述.sql`）；接口实现以 Scalar（`http://localhost:5146/scalar/v1`）自动生成为准。

---

## 变更时间线

### 2026-07-26 · 订单模块调整

脚本：`db/migrations/20260726__order_ticket_change.sql`

- `T_ORDER` 移除 `ACTUAL_AMOUNT` 字段与约束 `CHK_T_ORDER_ACTUAL`。
- `E_TICKET` 移除 `SESSION_ID`（改由订单明细 `ORDER_ITEM` 间接关联场次），删除相关外键与索引。
- `EXCHANGE_ITEM` 由「新座位/新票价」改为「新订单明细 `NEW_ORDER_ITEM_ID`」。
- 明确 `T_ORDER.ORDER_TYPE` 取值：`NORMAL / SPLIT / MERGE / EXCHANGE`。

### 2026-08-12 · 场次查询索引

脚本：`db/migrations/20260812_showsession_change.sql`

- `SHOW_SESSION` 新增复合索引：
  - `IDX_SHOW_SESSION_SHOW_STATUS (SHOW_ID, SESSION_STATUS, START_TIME)`
  - `IDX_SHOW_SESSION_SALE_TIME (SALE_START_TIME, SALE_END_TIME, SESSION_STATUS)`

### 2026-08-24 · 动态调价规则

脚本：`db/migrations/20260824__dynamic_pricing_rule.sql`

- 新增表 `DYNAMIC_PRICING_RULE`：`TRIGGER_TYPE`（`TIME_WINDOW / INVENTORY_RATE`）、`ADJUSTMENT_TYPE`（`DISCOUNT_RATE / AMOUNT_OFF / FIXED_PRICE`）、`PRIORITY`、`STATUS`。
- 对应接口：`POST /api/admin/sessions/{sessionId}/dynamic-pricing-rules`；下单时按场次生效规则实时计价。

### 2026-08-24 · 退票工作流支持

脚本：`db/migrations/20260824__refund_workflow_support.sql`

- `REFUND_REQUEST` 新增 `APPLIED_POLICY_ID`（命中的退票策略）、`APPLIED_SERVICE_FEE`（服务费）等字段。
- 电子票状态补齐退款/改签相关取值。
- 对应接口：`POST /api/orders/{orderId}/refunds/quote`（退票报价）、`POST /api/orders/{orderId}/refunds`（申请）、管理员 `POST /api/admin/refunds/{id}/approve|reject`（审核）。

### 2026-08-27 · 用户头像 URL

脚本：`db/migrations/20260827__sys_user_avatar_url.sql`

- `SYS_USER` 新增 `AVATAR_URL VARCHAR2(500)`。
- 对应接口：`POST /api/files/upload`（OSS 上传）→ `PUT /api/users/me/avatar`（持久化头像 URL）。

### 2026-08-30 · 改签工作流支持

脚本：`db/migrations/20260830__exchange_workflow_support.sql`

- 改签子订单、改签明细、差价支付等支持落地。
- 对应接口：`POST /api/orders/{orderId}/exchanges/quote`、`POST /api/orders/{orderId}/exchanges`、`POST /api/exchanges/{exchangeId}/pay`、管理员 `POST /api/admin/exchanges/{id}/approve|reject`。
- 改签契约：**仅同演出换场次**，目标座位与原票 **1:1 映射**。

### 2026-09-04 · 订单幂等键

脚本：`db/migrations/20260904__order_idempotency.sql`

- `T_ORDER` 新增 `IDEMPOTENCY_KEY` 与 `IDEMPOTENCY_REQUEST_HASH`。
- 唯一性采用「忽略 NULL 幂等键」的**函数唯一索引**（历史/改签子订单键为 NULL 不受影响）。
- 对应接口：`POST /api/orders` 要求 `Idempotency-Key` 请求头，服务端按（用户、幂等键、请求摘要）做安全重放与并发去重。

### 2026-09-05 · 订单事件出箱（Outbox）

脚本：`db/migrations/20260905__order_event_outbox.sql`

- 新增表 `ORDER_EVENT_OUTBOX`，支撑订单/退款事件的事务性发布。
- 事件类型：`OrderCreatedEvent`、`RefundApprovedEvent`、`RefundStatusChangedEvent`。
- 对应链路：订单/退款写入 Outbox → 后台 Worker 轮询发布（RabbitMQ 或进程内）→ 消费者 → SignalR 推送前端。

---

## 接口清单（当前实现）

> 完整字段与错误码以 Scalar/OpenAPI 为准，此处仅列路径与身份要求。

### 认证 `/api/auth`

| 方法 | 路径 | 身份 |
|------|------|------|
| POST | `/api/auth/register` | 匿名 |
| POST | `/api/auth/login` | 匿名 |
| POST | `/api/auth/refresh` | Refresh Token |
| POST | `/api/auth/logout` | JWT |
| POST | `/api/auth/logout-all` | JWT |
| GET | `/api/auth/sessions` | JWT |
| DELETE | `/api/auth/sessions/{sessionId}` | JWT |

### 用户 `/api/users`

| 方法 | 路径 | 身份 |
|------|------|------|
| PUT | `/api/users/me/avatar` | JWT |
| GET/POST | `/api/users/me/real-names` | JWT |
| PUT/DELETE | `/api/users/me/real-names/{realNameId}` | JWT |
| PATCH | `/api/users/me/real-names/{realNameId}/default` | JWT |

### 文件 `/api/files`

| 方法 | 路径 | 身份 |
|------|------|------|
| POST | `/api/files/upload` | JWT |

### 客户端演出/场次 `/api/client`

| 方法 | 路径 | 身份 |
|------|------|------|
| GET | `/api/client/shows` | 匿名 |
| GET | `/api/client/shows/{showId}` | 匿名 |
| GET | `/api/client/shows/{showId}/sessions` | 匿名 |
| GET | `/api/client/sessions/{sessionId}/pricing-strategies` | 匿名 |
| GET | `/api/client/shows/{showId}/marketing-contents` | 匿名 |

### 座位 `/api/sessions`

| 方法 | 路径 | 身份 |
|------|------|------|
| GET | `/api/sessions/{sessionId}/seat-map` | 匿名 |
| POST | `/api/sessions/{sessionId}/seat-locks` | JWT |
| POST | `/api/sessions/{sessionId}/seat-locks/release` | JWT |

### 订单 `/api/orders`

| 方法 | 路径 | 身份 |
|------|------|------|
| GET | `/api/orders` | JWT |
| GET | `/api/orders/{orderId}` | JWT |
| POST | `/api/orders`（需 `Idempotency-Key`） | JWT |
| PATCH | `/api/orders/{orderId}/cancel` | JWT |
| GET | `/api/orders/{orderId}/tickets` | JWT |
| GET | `/api/orders/{orderId}/payments` | JWT |
| POST | `/api/orders/{orderId}/payments/mock` | JWT |

### 退票 / 改签 `/api`

| 方法 | 路径 | 身份 |
|------|------|------|
| POST | `/api/orders/{orderId}/refunds/quote` | JWT |
| POST | `/api/orders/{orderId}/refunds` | JWT |
| GET | `/api/orders/{orderId}/refunds` | JWT |
| GET | `/api/refunds/{refundId}` | JWT |
| POST | `/api/orders/{orderId}/exchanges/quote` | JWT |
| POST | `/api/orders/{orderId}/exchanges` | JWT |
| GET | `/api/orders/{orderId}/exchanges` | JWT |
| GET | `/api/exchanges/{exchangeId}` | JWT |
| POST | `/api/exchanges/{exchangeId}/pay` | JWT |

### 管理端 `/api/admin`

| 方法 | 路径 | 说明 |
|------|------|------|
| POST/GET | `/api/admin/shows` | 演出增查 |
| GET/PUT/DELETE | `/api/admin/shows/{showId}` | 演出详情/改/删 |
| GET/POST | `/api/admin/shows/{showId}/sessions` | 场次列表/新增 |
| POST | `/api/admin/sessions/{sessionId}/pricing-strategies` | 票价策略 |
| POST | `/api/admin/sessions/{sessionId}/dynamic-pricing-rules` | 动态调价 |
| PUT | `/api/admin/sessions/{sessionId}/status` | 场次状态 |
| GET/POST | `/api/admin/seat-maps` | 座位图 |
| GET/PUT/DELETE | `/api/admin/seat-maps/{id}` | 座位图 |
| GET/POST | `/api/admin/seat-maps/{id}/sections` | 分区 |
| GET/PUT/DELETE | `/api/admin/seat-sections/{id}` | 分区 |
| GET/POST/PATCH | `/api/admin/seat-sections/{id}/seats` | 座位 |
| GET/PUT/DELETE | `/api/admin/seats/{id}` | 座位 |
| GET/POST | `/api/admin/seat-rules`（含 `/{id}`、`/{id}/scopes`） | 选座规则 |
| GET | `/api/admin/orders` | 订单列表 |
| GET | `/api/admin/orders/{orderId}` | 订单详情 |
| PATCH | `/api/admin/orders/{orderId}/cancel` | 取消订单 |
| POST | `/api/admin/orders/{orderId}/issue` | 补出票 |
| GET | `/api/admin/refunds`、`/{refundId}` | 退票申请 |
| POST | `/api/admin/refunds/{refundId}/approve`、`/reject` | 退票审核 |
| GET | `/api/admin/exchanges`、`/{exchangeId}` | 改签申请 |
| POST | `/api/admin/exchanges/{exchangeId}/approve`、`/reject` | 改签审核 |
| GET/POST | `/api/admin/refund-policies`、`PUT/{id}`、`PATCH/{id}/status` | 退票策略 |
| GET/POST | `/api/admin/exchange-policies`、`PUT/{id}`、`PATCH/{id}/status` | 改签策略 |
| POST | `/api/admin/tickets/redeem` | 电子票核销 |
| GET/POST/PUT/DELETE | `/api/admin/marketing-contents` | 营销内容 |

---

## 变更记录维护约定

- 新增接口/字段后，在本文件「变更时间线」追加一条记录（日期 + 脚本 + 接口）。
- 数据库变更一律走 `db/migrations/`，命名 `日期__模块描述.sql`。
- 接口字段与错误码以 Scalar 自动生成为最终准，本文档维护其无法覆盖的状态机、幂等、错误码语义等。
