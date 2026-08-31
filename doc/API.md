# SHOWTIME API 文档

> **状态说明**：后端当前已完成实体建模与 EF Core 映射，**尚未实现 Controller / Service 层**。本文档中的「通用约定」为团队待确认的统一规范，「规划中的接口」为基于数据库模型与开发计划的草案，**所有字段与路径在实现前均可能调整**。接口实现后，本文档应逐步替换为基于 Scalar/OpenAPI 自动生成的准确描述。

---

## 目录

1. [接口文档如何查看](#1-接口文档如何查看)
2. [通用约定](#2-通用约定)
3. [数据模型概览](#3-数据模型概览)
4. [状态枚举速查](#4-状态枚举速查)
5. [规划中的接口清单](#5-规划中的接口清单)

---

## 1. 接口文档如何查看

后端已集成 [Scalar](https://scalar.com/) 与 OpenAPI，开发环境下接口自动生成，无需手写：

| 内容 | 地址 |
|------|------|
| Scalar 可视化文档 | `http://localhost:5146/scalar/v1` |
| OpenAPI JSON | `http://localhost:5146/openapi/v1.json` |
| 健康检查 | `http://localhost:5146/` |

约定：**接口一旦实现，以 Scalar 自动生成的 OpenAPI 描述为准**；本文档负责维护其无法覆盖的「约定、错误码、状态机、规划清单」等补充信息。

---

## 2. 通用约定

> 以下为团队约定草案，待后端实现 Controller 后随代码统一落地。

### 2.1 Base URL

- 开发环境：`http://localhost:5146`
- 生产环境：以部署文档中的反向代理地址为准（见 [DEPLOYMENT.md](DEPLOYMENT.md)）

### 2.2 统一返回格式（草案）

所有接口统一返回 JSON，结构如下：

```json
{
  "code": 0,
  "message": "success",
  "data": {}
}
```

| 字段 | 类型 | 说明 |
|------|------|------|
| `code` | int | 业务状态码，`0` 表示成功，非 `0` 表示失败 |
| `message` | string | 提示信息（成功为 `"success"`，失败为具体原因） |
| `data` | object / array / null | 业务数据，失败时通常为 `null` |

分页查询时 `data` 采用统一分页结构：

```json
{
  "code": 0,
  "message": "success",
  "data": {
    "list": [],
    "total": 0,
    "page": 1,
    "pageSize": 10
  }
}
```

### 2.3 错误码表（草案）

| code | 含义 | 说明 |
|------|------|------|
| 0 | 成功 | 请求处理成功 |
| 400 | 参数错误 | 请求参数缺失、格式错误或校验不通过 |
| 401 | 未认证 | 未登录或 Token 缺失/失效 |
| 403 | 无权限 | 已认证但无该资源操作权限 |
| 404 | 资源不存在 | 请求的资源不存在 |
| 409 | 状态冲突 | 业务状态不允许当前操作（如已核销再次核销、座位已锁等） |
| 429 | 请求过于频繁 | 触发限流 |
| 500 | 服务器内部错误 | 未预期异常 |

### 2.4 认证约定（草案）

- 采用 **JWT Bearer Token**：登录成功后返回 `accessToken`，前端置于请求头 `Authorization: Bearer <token>`。
- 规划中的会话能力：Refresh Token 刷新、异地登录检测、会话状态管理（对应 `USER_SESSION` 表）。

### 2.5 命名与格式约定

| 项 | 约定 |
|----|------|
| 路径风格 | RESTful，资源名复数，如 `/api/orders`、`/api/shows` |
| 版本前缀 | `/api` 起步，必要时加 `/api/v1` |
| 主键 | 统一使用自增 `long` 类型 ID（对应 `NUMBER(19)`） |
| 时间格式 | 统一 ISO 8601，如 `2026-08-27T20:00:00`；纯日期为 `yyyy-MM-dd` |
| 金额格式 | 以「元」为单位的小数（`decimal`），如 `99.00` |
| 布尔 | 数据库用 `0/1` 或 `Y/N`，接口层建议统一为 `true/false` |

### 2.6 幂等与并发

- 支付、核销、退票、改签等**状态变更类接口必须保证幂等**：重复提交同一业务号（如 `orderNo` / `refundNo` / `exchangeNo`）不应产生重复副作用。
- 选座锁座涉及超卖防护，锁粒度「座位 × 场次」（见 `SEAT_LOCK` 表唯一索引）。

---

## 3. 数据模型概览

共 **36 张表**，按 4 大模块划分。完整字段与约束见 `db/baseline/merged_ddl.sql`，EF Core 实体映射见 `backend/Data/Configurations/`。

### 3.1 用户与权限模块（10 表）

| 表名 | 说明 | 关键字段 |
|------|------|----------|
| `ORG_STRUCTURE` | 组织机构 | ORG_CODE, ORG_TYPE(COMPANY/DEPT/TEAM/OTHER) |
| `SYS_USER` | 系统用户 | USER_NAME, PHONE, USER_TYPE(NORMAL/MEMBER/VIP), STATUS |
| `ROLE` | 角色 | ROLE_CODE, ROLE_NAME |
| `PERMISSION` | 权限 | PERM_CODE, RESOURCE_TYPE(MENU/BUTTON/API/DATA) |
| `USER_ROLE` | 用户-角色关联 | USER_ID, ROLE_ID |
| `ROLE_PERMISSION` | 角色-权限关联 | ROLE_ID, PERMISSION_ID |
| `USER_BLACKLIST` | 用户黑名单 | RISK_TYPE, RISK_SCORE, IS_PERMANENT |
| `USER_REAL_NAME` | 用户实名 | REAL_NAME, ID_CARD_NO, IS_VERIFIED |
| `OPERATION_LOG` | 操作日志 | OPERATION_MODULE, OPERATION_TYPE, IP_ADDRESS |
| `USER_SESSION` | 用户会话 | SESSION_TOKEN, STATUS(ACTIVE/EXPIRED/LOGOUT/LOCKED) |

### 3.2 演出与场次模块（9 表）

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

### 3.4 订单票务模块（10 表）

| 表名 | 说明 | 关键字段 |
|------|------|----------|
| `T_ORDER` | 订单主表 | ORDER_NO, ORDER_TYPE, ORDER_STATUS, 金额字段 |
| `ORDER_ITEM` | 订单明细 | SEAT_ID, PRICE_STRATEGY_ID, ITEM_STATUS, UNIT_PRICE |
| `PAYMENT` | 支付流水 | PAY_CHANNEL, PAY_STATUS, TRADE_NO |
| `E_TICKET` | 电子票 | QR_CODE, ANTI_FAKE_CODE, TICKET_STATUS |
| `REFUND_REQUEST` | 退票申请 | REFUND_TYPE, APPROVE_STATUS, REFUND_STATUS |
| `REFUND_ITEM` | 退票明细 | REFUND_ID, ORDER_ITEM_ID |
| `EXCHANGE_REQUEST` | 改签申请 | ORIG_SESSION_ID, TARGET_SESSION_ID, EXCHANGE_STATUS |
| `EXCHANGE_ITEM` | 改签明细 | ORDER_ITEM_ID, NEW_ORDER_ITEM_ID |
| `REFUND_POLICY` | 退票规则 | REFUND_DEADLINE_HOUR, REFUND_RATE, SERVICE_FEE |
| `EXCHANGE_POLICY` | 改签规则 | EXCHANGE_DEADLINE_HOUR, EXCHANGE_FEE, ALLOW_CROSS_SESSION |

---

## 4. 状态枚举速查

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

### 4.2 电子票状态 `E_TICKET.TICKET_STATUS`

| 值 | 含义 |
|----|------|
| `UNUSED` | 未使用（默认，待核销） |
| `USED` | 已核销 |
| `REFUNDED` | 已退票 |
| `EXCHANGED` | 已改签 |

> 核销相关字段：`CHECK_TIME`（核销时间）、`CHECK_DEVICE`（核销设备）、`CHECK_BY`（核销人员）。核销状态设计对应本周「订单票务：确定核销状态设计」任务，最终以该任务结论为准。

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
- `PRICE_STRATEGY.PRICE_TYPE`：`EARLY_BIRD` / `PRESALE` / `STANDARD` / `VIP` / `MEMBER`
- `SHOW.STATUS`：`DRAFT` / `PUBLISHED` / `UNPUBLISHED`；`AUDIT_STATUS`：`PENDING` / `APPROVED` / `REJECTED`

---

## 5. 规划中的接口清单

> **以下均为草案**，基于数据库模型与开发计划整理，用于前后端对齐需求。字段与路径在实现时可能与下表不一致；实现后请以 Scalar 自动生成为准并同步更新本节。

### 5.1 用户与权限

| 方法 | 路径 | 说明 | 对应本周任务 |
|------|------|------|--------------|
| POST | `/api/auth/register` | 用户注册 | — |
| POST | `/api/auth/login` | 登录（返回 JWT） | 埋点登录 |
| POST | `/api/auth/logout` | 登出 | — |
| GET/POST/PUT/DELETE | `/api/users/{id}/real-name` | 实名信息增删查改 | 实名 API 的增删查 |

### 5.2 演出与场次

| 方法 | 路径 | 说明 | 对应本周任务 |
|------|------|------|--------------|
| GET | `/api/shows` | 演出列表（分页/筛选） | — |
| POST | `/api/shows` | 发布演出 | — |
| GET | `/api/shows/{id}/sessions` | 某演出场次列表 | — |
| GET | `/api/sessions/{id}/prices` | 场次票价策略 | 动态调价规则配置化 |
| POST | `/api/sessions/{id}/prices` | 配置动态调价规则 | 解决 PriceStrategy TODO |

### 5.3 座位区域

| 方法 | 路径 | 说明 | 对应本周任务 |
|------|------|------|--------------|
| POST | `/api/seat-maps` | 创建座位图 | 座位图编辑器 |
| POST | `/api/seat-sections/{id}/seats/batch` | 批量建座 | 批量操作 API |
| PUT | `/api/seats/batch-price` | 批量改价 | 批量操作 API |
| PUT | `/api/seats/batch-status` | 批量上下架 | 批量操作 API |

### 5.4 订单票务

| 方法 | 路径 | 说明 | 对应本周任务 |
|------|------|------|--------------|
| POST | `/api/orders` | 下单（埋点下单） | 埋点下单 |
| GET | `/api/orders/{orderId}` | 订单详情（前端 orderId 路由） | orderId 路由 |
| POST | `/api/orders/{orderId}/pay` | 支付（Mock） | — |
| POST | `/api/orders/{orderId}/refund` | 申请退票 | 退票 |
| POST | `/api/orders/{orderId}/exchange` | 申请改签 | 改签 |
| POST | `/api/tickets/{eticketNo}/check` | 电子票核销 | 核销状态设计 |

### 5.5 前端对接备注

- **orderId 路由**：前端需新增 `/order/:orderId` 详情路由，后端提供 `GET /api/orders/{orderId}` 支撑「查看详情现在打不开」问题。
- **统一请求层**：前端需封装 Axios 实例（Base URL、Token 注入、统一错误处理、统一返回格式解析），详见前端 README。
- **批量操作**：管理端批量建座/改价/上下架需与前端「座位图编辑器」需求对齐，接口入参与返回结构以联调结论为准。

---

## 附录：变更日志

| 日期 | 变更 | 来源 |
|------|------|------|
| 2026-07-26 | 订单模块调整：`T_ORDER` 移除 `ACTUAL_AMOUNT`；`E_TICKET` 移除 `SESSION_ID`（改由订单明细间接关联）；`EXCHANGE_ITEM` 由「新座位/新票价」改为「新订单明细 `NEW_ORDER_ITEM_ID`」 | `db/migrations/20260726__order_ticket_change.sql` |
