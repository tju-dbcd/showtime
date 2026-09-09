# SHOWTIME API 文档

> **状态说明**：后端 Controller / Service 层**已全部实现**。接口定义以开发环境 Scalar 自动生成的 OpenAPI 为准；**完整的当前接口清单与变更时间线见 [API-CHANGELOG.md](API-CHANGELOG.md)**。本文档只维护 OpenAPI 无法覆盖的「约定、错误码、状态机、数据模型补充」等信息。
>
> 前端请求层由 `frontend/src/api/openapi.json` 快照生成类型（`openapi-fetch`），CI 会校验快照与后端输出一致。

---

## 目录

1. [接口文档如何查看](#1-接口文档如何查看)
2. [通用约定](#2-通用约定)
3. [数据模型概览](#3-数据模型概览)
4. [状态枚举速查](#4-状态枚举速查)
5. [当前接口清单](#5-当前接口清单)

---

## 1. 接口文档如何查看

后端已集成 [Scalar](https://scalar.com/) 与 OpenAPI，**仅 `Development` / `Testing` 环境**自动映射（见 `backend/Program.cs`）：

| 内容 | 地址 |
|------|------|
| Scalar 可视化文档 | `http://localhost:5146/scalar/v1` |
| OpenAPI JSON | `http://localhost:5146/openapi/v1.json` |
| 健康检查 | `http://localhost:5146/` |

约定：**接口一律以 Scalar 自动生成的 OpenAPI 描述为准**；本文档维护其无法覆盖的「约定、错误码、状态机」等补充信息。

---

## 2. 通用约定

### 2.1 Base URL

- 开发环境：`http://localhost:5146`
- 生产环境：同域反代（Nginx 把静态资源与 `/api` 放同一域名），见 [DEPLOYMENT.md](DEPLOYMENT.md)
- 跨域直连不推荐：后端**未配置 CORS**

### 2.2 统一返回格式

所有接口统一返回 JSON 信封，结构如下：

```json
{
  "success": true,
  "data": {},
  "code": null,
  "message": "success"
}
```

| 字段 | 类型 | 说明 |
|------|------|------|
| `success` | bool | 是否成功 |
| `data` | object / array / null | 业务数据；失败时为 `null` |
| `code` | string / null | **业务错误码**（字符串），仅失败时非空；成功为 `null` |
| `message` | string | 提示信息（英文，前端可直接展示） |

失败示例（HTTP 401 + 信封）：

```json
{
  "success": false,
  "data": null,
  "code": "AUTH_INVALID_CREDENTIALS",
  "message": "The account or password is incorrect."
}
```

分页查询时 `data` 采用统一分页结构（字段名以具体接口/OpenAPI 为准，常见为 `items` / `totalCount`）：

```json
{
  "success": true,
  "data": {
    "items": [],
    "totalCount": 0
  },
  "code": null,
  "message": "success"
}
```

### 2.3 错误码表

**HTTP 状态码表达语义，`code` 为具体业务错误码字符串**：

| HTTP | 语义 | 典型业务错误码（`code`） |
|------|------|------|
| 400 | 参数错误 / 模型校验失败 | `VALIDATION_FAILED`、`ORDER_INVALID_ITEMS`、`ORDER_INVALID_IDEMPOTENCY_KEY` |
| 401 | 未认证 / Token 失效 / 凭证错误 | `AUTH_INVALID_CREDENTIALS`、`AUTH_REFRESH_EXPIRED`、`AUTH_SESSION_LOCKED` |
| 403 | 无权限 / 账号被禁用 | `AUTH_ACCOUNT_DISABLED`、`AUTH_ACCOUNT_LOCKED` |
| 404 | 资源不存在 | `ORDER_NOT_FOUND`、`AUTH_SESSION_NOT_FOUND` |
| 409 | 状态冲突 / 重复提交 | `AUTH_USERNAME_TAKEN`、`SEAT_LOCK_CONFLICT`、`ORDER_IDEMPOTENCY_CONFLICT`、`PAYMENT_ALREADY_SUCCEEDED` |
| 413 | 文件过大 | `FILE_TOO_LARGE` |
| 429 | 触发限流（响应含 `Retry-After` 秒数） | `RATE_LIMIT_EXCEEDED` |
| 500 | 服务器内部错误 | `INTERNAL_ERROR` |
| 503 | 依赖未就绪 / 存储未配置 | `FILE_STORAGE_NOT_CONFIGURED`、`AUTH_DEFAULT_ROLE_UNAVAILABLE` |

完整业务错误码以 Scalar/OpenAPI 与代码为准；速查表见 [FAQ.md](FAQ.md) 附录。

### 2.4 认证与会话约定

- 采用 **JWT Bearer Token**：登录成功后返回 15 分钟有效的 `accessToken`，前端置于请求头 `Authorization: Bearer <token>`。
- Access Token 包含用户 ID、用户名、角色和 `sid` 会话 ID；每次鉴权都会验证 `USER_SESSION` 仍为 `ACTIVE`，所以退出或踢出后旧 JWT 立即失效。
- 登录同时返回 7 天有效的 `refreshToken`。刷新后 Refresh Token 必须轮换，旧 Token 立即失效；检测到旧 Token 重用时锁定该会话。
- 服务端只保存 Refresh Token 的 SHA-256 摘要，接口、会话列表和日志均不返回摘要。
- 每个用户只允许一个活跃会话。再次登录会使旧会话失效；IP 或 User-Agent 变化时旧会话标记为 `LOCKED` 和风险会话。
- Token 通过 JSON 请求/响应传递；前端不得把 Refresh Token 输出到日志。

登录响应示例：

```json
{
  "success": true,
  "data": {
    "accessToken": "<access-token>",
    "tokenType": "Bearer",
    "expiresIn": 900,
    "expiresAtUtc": "2026-09-03T08:15:00Z",
    "refreshToken": "<refresh-token>",
    "refreshTokenExpiresAtUtc": "2026-09-10T08:00:00Z",
    "user": {}
  },
  "code": null,
  "message": "Login succeeded."
}
```

会话接口：

| 方法 | 路径 | 身份要求 | 说明 |
|------|------|----------|------|
| POST | `/api/auth/refresh` | Refresh Token | 轮换并返回一组新 Token |
| POST | `/api/auth/logout` | JWT | 退出当前会话 |
| POST | `/api/auth/logout-all` | JWT | 退出当前用户的全部活跃会话 |
| GET | `/api/auth/sessions` | JWT | 查询当前用户的会话历史与状态 |
| DELETE | `/api/auth/sessions/{sessionId}` | JWT | 踢出自己的指定会话；他人或未知会话统一返回 404 |

### 2.4.1 API 限流

| 请求类型 | 分区 | 固定窗口额度 |
|----------|------|--------------|
| 登录 | IP | 5 次/分钟 |
| 注册 | IP | 3 次/分钟 |
| 刷新 Token | IP | 10 次/分钟 |
| 已认证普通请求 | 用户 ID | 120 次/分钟 |
| 匿名普通请求 | IP | 60 次/分钟 |

登录、注册和刷新只计入各自专属额度，不重复消耗匿名普通额度。超限响应为 HTTP 429，并包含整数秒 `Retry-After`：

```json
{
  "success": false,
  "data": null,
  "code": "RATE_LIMIT_EXCEEDED",
  "message": "Too many requests. Please retry later."
}
```

### 2.5 命名与格式约定

| 项 | 约定 |
|----|------|
| 路径风格 | RESTful，资源名复数，如 `/api/orders`、`/api/shows` |
| 版本前缀 | `/api` 起步 |
| 主键 | 统一使用自增 `long` 类型 ID（对应 `NUMBER(19)`） |
| 时间格式 | ISO 8601，统一 UTC 带 `Z`，如 `2026-09-09T12:00:00Z`（库内 TIMESTAMP 也存 UTC，见 `db/migrations/20260909__utc_timestamps.sql`） |
| 金额格式 | 以「元」为单位的小数（`decimal`），如 `99.00` |
| 枚举 | 序列化为成员名字符串（与数据库 CHECK 约束取值一致），如 `PENDING_PAY` |

### 2.6 幂等与并发

- 支付、核销、退票、改签等**状态变更类接口必须保证幂等**：重复提交同一业务号（如 `orderNo` / `refundNo` / `exchangeNo`）不应产生重复副作用。
- **创建订单必须携带 `Idempotency-Key` 请求头**（前端每次下单生成 `crypto.randomUUID()`）：服务端按（用户、幂等键、请求摘要）去重，同键同摘要返回原订单，同键不同摘要返回 409 `ORDER_IDEMPOTENCY_CONFLICT`。
- 选座锁座涉及超卖防护，锁粒度「座位 × 场次」：DB `SEAT_LOCK` 表活动锁唯一索引最终仲裁，Redis 仅作前置加速（`Redis:SeatLockTtlSeconds`，默认 600 秒）。
- 改签契约为**同演出换场次、1:1 换票**：每个目标座位携带唯一原票明细 ID（`originalOrderItemId`）。

---

## 3. 数据模型概览

数据库**基线 36 张表**，按 4 大模块划分；应用 `db/migrations/` 全部增量后为 **38 张表**（新增 `DYNAMIC_PRICING_RULE`、`T_ORDER_EVENT_OUTBOX`）。完整字段与约束见 `db/baseline/merged_ddl.sql`（**四个模块脚本为早期拆分稿，勿混用**），EF Core 实体映射见 `backend/Data/Configurations/`。

### 3.1 用户与权限模块（10 表）

| 表名 | 说明 | 关键字段 |
|------|------|----------|
| `ORG_STRUCTURE` | 组织机构 | ORG_CODE, ORG_TYPE(COMPANY/DEPT/TEAM/OTHER) |
| `SYS_USER` | 系统用户 | USER_NAME, PHONE, USER_TYPE(NORMAL/MEMBER/VIP), STATUS, AVATAR_URL |
| `ROLE` | 角色 | ROLE_CODE, ROLE_NAME |
| `PERMISSION` | 权限 | PERM_CODE, RESOURCE_TYPE(MENU/BUTTON/API/DATA) |
| `USER_ROLE` | 用户-角色关联 | USER_ID, ROLE_ID |
| `ROLE_PERMISSION` | 角色-权限关联 | ROLE_ID, PERMISSION_ID |
| `USER_BLACKLIST` | 用户黑名单 | RISK_TYPE, RISK_SCORE, IS_PERMANENT |
| `USER_REAL_NAME` | 用户实名 | REAL_NAME, ID_CARD_NO（加密存储）, IS_VERIFIED |
| `OPERATION_LOG` | 操作日志 | OPERATION_MODULE, OPERATION_TYPE, IP_ADDRESS |
| `USER_SESSION` | 用户会话 | SESSION_TOKEN, STATUS(ACTIVE/EXPIRED/LOGOUT/LOCKED) |

### 3.2 演出与场次模块（基线 9 表 + `DYNAMIC_PRICING_RULE`）

| 表名 | 说明 | 关键字段 |
|------|------|----------|
| `CATEGORY` | 演出分类 | CATEGORY_NAME, PARENT_ID |
| `TAG` | 标签 | TAG_NAME, COLOR |
| `SHOW` | 演出主表 | STATUS(DRAFT/PUBLISHED/UNPUBLISHED), AUDIT_STATUS |
| `SHOW_TAG` | 演出-标签关联 | SHOW_ID, TAG_ID |
| `VENUE` | 场馆 | VENUE_NAME, STATUS(ENABLED/DISABLED) |
| `SHOW_SESSION` | 场次 | START_TIME, SESSION_STATUS, SEAT_MAP_ID |
| `PRICE_STRATEGY` | 票价策略 | PRICE_TYPE, PRICE, PRIORITY, QUOTA |
| `PURCHASE_LIMIT` | 限购规则 | MAX_BUY_COUNT, LIMIT_TYPE(TICKET/ORDER) |
| `MARKETING_CONTENT` | 营销内容 | CONTENT_TYPE(NOTICE/AD/PROMOTION) |
| `DYNAMIC_PRICING_RULE` | 动态调价规则（迁移新增） | TRIGGER_TYPE, ADJUSTMENT_TYPE, PRIORITY, STATUS |

### 3.3 座位区域模块（7 表）

| 表名 | 说明 | 关键字段 |
|------|------|----------|
| `SEAT_MAP` | 座位图 | MAP_CODE, MAP_VERSION, IS_DEFAULT, MAP_STATUS |
| `SEAT_SECTION` | 座位分区 | SECTION_TYPE(NORMAL/VIP/ACCESSIBLE/STANDING), IS_SELLABLE |
| `SEAT` | 座位 | ROW_CODE, SEAT_NO, ROW_INDEX, COL_INDEX, X/Y_COORD, SEAT_STATUS |
| `SEAT_LOCK` | 座位锁 | LOCK_STATUS(ACTIVE/RELEASED/EXPIRED/CONVERTED) |
| `SEAT_RESERVATION` | 座位预订 | RESERVATION_TYPE(ORDER/SYSTEM/VIP), RESERVATION_STATUS |
| `SEAT_RULE` | 选座规则 | RULE_TYPE(CONTINUOUS/NO_SINGLE_LEFT/LIMIT_COUNT/SECTION_LIMIT) |
| `SEAT_RULE_SCOPE` | 规则适用范围 | SCOPE_TYPE(MAP/SECTION) |

### 3.4 订单票务模块（基线 10 表 + `T_ORDER_EVENT_OUTBOX`）

| 表名 | 说明 | 关键字段 |
|------|------|----------|
| `T_ORDER` | 订单主表 | ORDER_NO, ORDER_TYPE, ORDER_STATUS, IDEMPOTENCY_KEY |
| `ORDER_ITEM` | 订单明细 | SEAT_ID, PRICE_STRATEGY_ID, ITEM_STATUS, UNIT_PRICE |
| `PAYMENT` | 支付流水 | PAY_CHANNEL, PAY_STATUS, TRADE_NO |
| `E_TICKET` | 电子票 | QR_CODE, ANTI_FAKE_CODE, TICKET_STATUS |
| `REFUND_REQUEST` | 退票申请 | REFUND_TYPE, APPROVE_STATUS, REFUND_STATUS |
| `REFUND_ITEM` | 退票明细 | REFUND_ID, ORDER_ITEM_ID |
| `EXCHANGE_REQUEST` | 改签申请 | ORIG_SESSION_ID, TARGET_SESSION_ID, EXCHANGE_STATUS |
| `EXCHANGE_ITEM` | 改签明细 | ORDER_ITEM_ID, NEW_ORDER_ITEM_ID |
| `REFUND_POLICY` | 退票规则 | REFUND_DEADLINE_HOUR, REFUND_RATE, SERVICE_FEE |
| `EXCHANGE_POLICY` | 改签规则 | EXCHANGE_DEADLINE_HOUR, EXCHANGE_FEE, ALLOW_CROSS_SESSION |
| `T_ORDER_EVENT_OUTBOX` | 订单事件出箱（迁移新增） | 支撑订单/退款事件的事务性发布 |

---

## 4. 状态枚举速查

枚举成员名称与数据库 CHECK 约束取值一致（完整定义见 `backend/Common/Enums.cs`）。

### 4.1 订单状态 `T_ORDER.ORDER_STATUS`

| 值 | 含义 |
|----|------|
| `PENDING_PAY` | 待支付 |
| `PAID` | 已支付 |
| `ISSUED` | 已出票 |
| `PART_REFUND` | 部分退款 |
| `REFUNDED` | 已退款 |
| `CANCELLED` | 已取消 |

订单类型 `ORDER_TYPE`：`NORMAL` 普通 / `SPLIT` 拆单 / `MERGE` 合单 / `EXCHANGE` 改签子订单。
订单明细状态 `ORDER_ITEM.ITEM_STATUS`：`NORMAL` / `REFUNDING` / `REFUNDED` / `EXCHANGING` / `EXCHANGED`。

### 4.2 电子票状态 `E_TICKET.TICKET_STATUS`

| 值 | 含义 |
|----|------|
| `UNUSED` | 未使用（默认，待核销） |
| `REFUNDING` | 退款处理中 |
| `USED` | 已核销 |
| `REFUNDED` | 已退票 |
| `EXCHANGING` | 改签处理中 |
| `EXCHANGED` | 已改签 |

> 核销相关字段：`CHECK_TIME`（核销时间）、`CHECK_DEVICE`（核销设备）、`CHECK_BY`（核销人员）。

### 4.3 支付状态 `PAYMENT.PAY_STATUS`

| 值 | 含义 |
|----|------|
| `PENDING` | 待支付 |
| `SUCCESS` | 支付成功 |
| `FAIL` | 支付失败 |
| `CLOSED` | 已关闭 |

支付渠道 `PAY_CHANNEL`：`ALIPAY` / `WECHAT` / `UNIONPAY` / `BALANCE`。

### 4.4 场次状态 `SHOW_SESSION.SESSION_STATUS`

| 值 | 含义 |
|----|------|
| `UPCOMING` | 未开售 |
| `PRESALE` | 预售 |
| `ONSALE` | 在售 |
| `SOLD_OUT` | 售罄 |
| `ENDED` | 已结束 |

### 4.5 退票/改签状态

`APPROVE_STATUS`（审核状态）：`PENDING` 待审 / `APPROVED` 通过 / `REJECTED` 拒绝

`REFUND_STATUS` / `EXCHANGE_STATUS`（执行状态）：`PENDING` / `PROCESSING` / `COMPLETED` / `FAILED`

退票类型 `REFUND_TYPE`：`FULL` 全退 / `PART` 部分退。

### 4.6 座位锁 `SEAT_LOCK.LOCK_STATUS`

| 值 | 含义 |
|----|------|
| `ACTIVE` | 锁定中 |
| `RELEASED` | 已释放 |
| `EXPIRED` | 已过期 |
| `CONVERTED` | 已转正式占座 |

### 4.7 其他常用枚举

- `SYS_USER.USER_TYPE`：`NORMAL` / `MEMBER` / `VIP`
- `SEAT.SEAT_STATUS`：`ENABLED` / `DISABLED` / `MAINTENANCE`
- `PRICE_STRATEGY.PRICE_TYPE`：`EARLY_BIRD` / `PRESALE` / `STANDARD` / `VIP` / `MEMBER`；`STATUS`：`ENABLED` / `DISABLED`
- `SHOW.STATUS`：`DRAFT` / `PUBLISHED` / `UNPUBLISHED`；`AUDIT_STATUS`：`PENDING` / `APPROVED` / `REJECTED`

---

## 5. 当前接口清单

> 以下按模块列出**主要接口族**；逐条方法与字段以 Scalar 自动生成的 OpenAPI 为准，完整变更记录见 [API-CHANGELOG.md](API-CHANGELOG.md)。

### 5.1 用户与权限

| 方法 | 路径 | 说明 |
|------|------|------|
| POST | `/api/auth/register` | 用户注册（限流 3 次/分/IP） |
| POST | `/api/auth/login` | 登录（返回 Access + Refresh Token；限流 5 次/分/IP） |
| POST | `/api/auth/refresh` | 轮换 Refresh Token |
| POST | `/api/auth/logout` / `/logout-all` | 退出当前/全部会话 |
| GET/DELETE | `/api/auth/sessions`、`/api/auth/sessions/{sessionId}` | 查询/踢出本人会话 |
| PUT | `/api/users/me/avatar` | 更新头像（URL 持久化） |
| GET/POST/PUT/DELETE/PATCH | `/api/users/me/real-names*` | 实名信息增删改查、设默认 |
| POST | `/api/files/upload` | 统一文件上传（后端代理 OSS/本地磁盘） |

### 5.2 演出与场次（客户端）

| 方法 | 路径 | 说明 |
|------|------|------|
| GET | `/api/categories` | 演出分类列表 |
| GET | `/api/client/shows` | 已发布演出列表（分页/筛选） |
| GET | `/api/client/shows/{showId}` | 演出详情 |
| GET | `/api/client/shows/{showId}/sessions` | 某演出可售票场次 |
| GET | `/api/client/sessions/{sessionId}/pricing-strategies` | 场次票价/分区信息 |
| GET | `/api/client/shows/{showId}/marketing-contents` | 演出关联营销内容 |

### 5.3 座位与选座

| 方法 | 路径 | 说明 |
|------|------|------|
| GET | `/api/sessions/{sessionId}/seat-map` | 场次座位图（含占用状态） |
| POST | `/api/sessions/{sessionId}/seat-locks` | 锁座（Redis + DB 双重判定） |
| POST | `/api/sessions/{sessionId}/seat-locks/release` | 释放锁座 |

### 5.4 订单、退票、改签、票务

| 方法 | 路径 | 说明 |
|------|------|------|
| GET/POST | `/api/orders`、`/api/orders/{orderId}` | 订单列表/详情（创建需 `Idempotency-Key`） |
| PATCH | `/api/orders/{orderId}/cancel` | 取消待支付订单 |
| GET | `/api/orders/{orderId}/tickets` | 订单电子票列表 |
| GET/POST | `/api/orders/{orderId}/payments`、`/payments/mock` | 支付流水 / Mock 支付 |
| POST | `/api/orders/{orderId}/refunds/quote`、`/refunds` | 退票报价 / 申请 |
| GET | `/api/orders/{orderId}/refunds`、`/api/refunds/{refundId}` | 退票记录/详情 |
| POST | `/api/orders/{orderId}/exchanges/quote`、`/exchanges` | 改签报价 / 申请（同演出 1:1） |
| GET | `/api/orders/{orderId}/exchanges`、`/api/exchanges/{exchangeId}` | 改签记录/详情 |
| POST | `/api/exchanges/{exchangeId}/pay` | 支付改签差价 |

### 5.5 管理端（`/api/admin`，需 Admin 角色）

| 模块 | 主要路径 |
|------|----------|
| 用户管理 | `GET/POST /api/admin/users`、`DELETE /api/admin/users/{userId}` |
| 演出/场次 | `/api/admin/shows*`（含审核 `PUT .../{showId}/audit-status`）、`/api/admin/sessions*`（票价策略、动态调价、状态） |
| 座位 | `/api/admin/venues`、`/api/admin/seat-maps*`、`/api/admin/seat-sections*`、`/api/admin/seats*`、`/api/admin/seat-rules*`（含 scopes） |
| 订单 | `GET /api/admin/orders*`、`PATCH .../cancel`、`POST .../issue` |
| 退票 | `GET /api/admin/refunds*`、`POST .../{id}/approve|reject` |
| 改签 | `GET /api/admin/exchanges*`、`POST .../{id}/approve|reject` |
| 策略 | `/api/admin/refund-policies*`、`/api/admin/exchange-policies*` |
| 核销 | `POST /api/admin/tickets/redeem` |
| 营销 | `/api/admin/marketing-contents*` |

### 5.6 前端对接现状

- 前端路由已包含 `/order/:id`（订单详情）、`/seat-selection/:eventId`（选座）等页面，详情页可直接打开。
- 前端统一请求层已实现（`frontend/src/api/request.ts`，openapi-fetch + Token 注入 + 401 跳转 + 统一错误提示）。
- 管理端各页面已覆盖用户管理、退票/改签审核与策略、电子票核销等入口，见 [用户操作手册-管理员.md](用户操作手册-管理员.md)。

---

## 附录：变更日志

完整的逐次接口/数据库变更见 **[API-CHANGELOG.md](API-CHANGELOG.md)**（含每次变更对应的 `db/migrations/` 脚本）。
