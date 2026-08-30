# 审计 / 操作日志设计（Audit & Operation Log Design）

> 状态：领域事件通道已落地（里程碑 4「日志可用」验收）；HTTP 级全量审计为可选未实现。
> 关联实现：`backend/Services/OrderTicket/DbOperationTicketAuditSink.cs`、`backend/Data/Configurations/UserPermission/OperationLogConfiguration.cs`、`db/baseline/merged_ddl.sql` 第 5-3 节。

## 1. 背景与目标

审计日志回答一个事实性问题：**谁在什么时间、以什么身份，对哪笔业务执行了什么关键操作**。
用途：管理员查询（如"谁在什么时间用什么设备核销了哪张票"、退票审核留痕）与安全事件/纠纷排查。

两份候选数据来源：

| 来源 | 语义 | 示例 |
|---|---|---|
| 领域事件 | 业务级事实（业务已确认的结果） | `REFUND_APPROVED`、`TICKET_REDEEMED` |
| HTTP 请求 | 传输级快照（URL/参数/IP/UA/耗时） | `POST /api/tickets/redeem` + 请求参数 |

## 2. 通道与归属决策（C1）

**决策：单表复用 `OPERATION_LOG`，用 `OPERATION_MODULE` 区分通道。** 不拆两张表。

理由：

- 两张表的共性字段（时间、用户、操作类型、结果/错误）重复度高，拆分收益低；
- 单表查询免跨表关联（管理员后台统一"操作记录"视图）；
- `OPERATION_MODULE`（VARCHAR2(50)）+ `OPERATION_TYPE`（VARCHAR2(30)）足以标识通道与动作。

| 通道 | OPERATION_MODULE | 写入方 | 字段分工 |
|---|---|---|---|
| 领域事件（已落地） | `ORDER_TICKET` | `DbOperationTicketAuditSink` | `USER_NAME`=操作人、`OPERATION_TYPE`=事件名、`REQUEST_PARAMS`=事件 JSON 快照（含 OrderId/RefundId/ActualRefund/Metadata）、`STATUS=true`、`CREATE_BY`=操作人 |
| HTTP 全量（可选，未实现） | `HTTP` 等 | ASP.NET Core 中间件 | `REQUEST_URL`/`REQUEST_PARAMS`(脱敏)/`RESPONSE_RESULT`(截断)/`IP_ADDRESS`/`USER_AGENT`/`COST_TIME`/`STATUS`/`ERROR_MSG` |

约束：`OPERATION_TYPE` ≤ 30 字符、`OPERATION_MODULE` ≤ 50 字符（与 DDL/EF 配置一致）。

## 3. 领域事件通道（已落地）

- 事件清单（6 种，写入口 `WriteAuditSafelyAsync` → sink）：
  `ADMIN_TICKET_ISSUED`、`PAYMENT_TICKET_ISSUED`、`REFUND_REQUESTED`、`REFUND_APPROVED`、`REFUND_REJECTED`、`TICKET_REDEEMED`。
- 写语义：审计调用均发生在业务保存/事务提交**之后**；sink 经 `IDbContextFactory<AppDbContext>` 创建**独立 DbContext 实例**追加写入，
  审计失败只触发 `logger.LogWarning`（兜底痕迹已由 Serilog 文件日志承接），**绝不回滚或污染业务事务**。
- 主键：`LOG_ID` 由 `TRG_BIU_OPERATION_LOG` 触发器 + `SEQ_OPERATION_LOG` 生成（测试 SQLite 环境用 AUTOINCREMENT）。
- 事件序列化：整个事件 record（含发生的业务时间 `OccurredAt`）JSON 存 `REQUEST_PARAMS`，保证快照保真可还原。
- 查询：`IDX_OP_LOG_TIME` / `IDX_OP_LOG_TYPE` / `IDX_OP_LOG_USER` / `IDX_OP_LOG_SHOW` 已建。

## 4. 保留策略与容量（C2）

当前无分区、无保留约束（建库后靠外部清理）。落地策略：

1. **保留周期：90 天**。
2. **清理作业**：每日低频任务执行
   `DELETE FROM OPERATION_LOG WHERE CREATE_TIME < SYSDATE - 90`（走 `IDX_OP_LOG_TIME`）；
   建议独立后台定时任务（IHostedService）或外部调度（cron），不要在请求内清理。
3. **容量演进**：领域事件快照为 1KB 级小 JSON，量级可控；若将来启用 HTTP 级全量审计导致容量显著放大，
   升级为按月 `RANGE PARTITION ON (CREATE_TIME)`（OPERATION_LOG 需 DDL 改造），清理改为 `DROP PARTITION` 避免大 DELETE。
4. **脱敏规则（落库前强制）**：
   - 敏感键清单：`password`、`token`、`secret`、`accessKey`、`authorization`、证件号等；
   - 领域事件 `Metadata` 由调用方控制，禁止写入上述键；
   - HTTP 级审计中间件实现时对请求参数做字段级脱敏（值替换为 `******`），`RESPONSE_RESULT` 截断（如 4KB）并同样脱敏。
5. **已知边界**：当前无"审计日志不可删除"的合规级保护（管理员直连库可删）；如需强审计合规，
   应限制 `OPERATION_LOG` 的 DELETE 权限或改为归档标记，记为此处边界。

## 5. HTTP 级全量审计（可选，未实现）

- 触发条件：管理员需要传输级视图（"用户在何时访问了哪个 URL、耗时多少、什么设备/IP"）。
- 实现要点：全局中间件记录上述字段族；`password`/`token` 请求字段脱敏；响应体按需截断；与领域事件并存（`OPERATION_MODULE` 区分），互不影响。
- 当前决策：**暂不实现**——领域事件已覆盖关键业务操作的审计目标；HTTP 全量会显著放大 CLOB 容量，且需要补查询 API，收益/成本比暂不划算。

## 6. 现状与后续待办

| 项 | 状态 |
|---|---|
| 领域事件落库 `OPERATION_LOG`（替代 Null sink） | ✅ 已落地 |
| `DbSet<OperationLog>` / EF 映射 | ✅ 已落地 |
| Serilog 结构化日志（控制台 + 文件滚动） | ✅ 已落地（里程碑 5 Loki+Grafana 数据源） |
| 90 天清理作业 | ⬜ 待办（独立定时任务或外部 cron） |
| 管理端"操作记录"查询 API | ⬜ 待办（可读 `OPERATION_LOG` 分页查询 + 按类型/时间/用户过滤） |
| HTTP 级全量审计中间件 | ⬜ 可选，默认不启用 |