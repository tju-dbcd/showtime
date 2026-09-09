# SHOWTIME —— 演出活动票务管理平台

本项目是一个演出售票系统，主要用于实现演出活动的票务管理与在线购票功能。系统围绕用户与权限管理、演出信息发布、场次管理、座位安排、订单处理、支付核销等核心业务展开，旨在为用户提供便捷的购票体验，同时协助管理方实现票务数据的统一管理与实时统计分析。

---

## 技术栈

| 层次 | 技术 | 说明 |
|------|------|------|
| 后端框架 | ASP.NET Core（.NET 10）+ C# | Web API |
| ORM | Entity Framework Core | 使用 `Oracle.EntityFrameworkCore` 驱动 |
| 数据库 | Oracle XE 21c | 基线 36 张表 + 迁移后当前 38 张，Schema 归 `APP_OWNER` |
| 认证 | JWT（Access + Refresh Token） | 单活会话、异地登录检测、会话管理 |
| 缓存/锁 | Redis | 选座锁前置判定（加速层，可降级纯 Oracle） |
| 消息通知 | SignalR + RabbitMQ（可选） | 订单/退款实时通知；RabbitMQ 默认关闭 |
| 文件存储 | 阿里云 OSS / 本地磁盘 | 后端代理上传，前端不接触 AccessKey |
| 日志 | Serilog | 控制台 + 文件滚动输出 |
| API 文档 | OpenAPI + Scalar | 仅开发环境自动生成，快照在 `frontend/src/api/openapi.json` |
| 前端 | React 19 + TypeScript + Vite 8 | SPA |
| UI 组件库 | Ant Design 6 | — |
| 前端路由 | React Router 7 | — |
| HTTP 客户端 | openapi-fetch | 由 `frontend/src/api/openapi.json` 生成类型 |
| 测试 | xUnit（后端）、Playwright（前端 E2E） | E2E 通过 `page.route` mock API，不连真实后端 |
| 代码检查 | Oxlint（前端）/ dotnet format（后端） | — |

---

## 功能点

**用户与权限管理**
1. 多角色权限动态配置
2. 组织架构与数据隔离
3. 操作日志与审计追踪
4. 会话安全与风险控制
5. 用户行为分析与画像

**演出信息发布**
1. 演出信息发布与审核
2. 演出分类与标签管理
3. 演出热度与推荐算法
4. 演出关联营销内容管理

**场次管理**
1. 场次批量生成与冲突检测
2. 场次动态调价策略
3. 场次状态自动化管理
4. 场次限购策略配置
5. 场次销售数据实时看板

**座位安排**
1. 可视化座位图编辑器
2. 座位库存动态锁定与释放
3. 座位分组规则
4. 座位预留与分配管理

**订单处理**
1. 订单智能拆单与合并
2. 退票与改签策略引擎
3. 订单生命周期管理
4. 电子票务生成与防伪

---

## 项目现状

> 当前处于开发阶段，进度以 [PLAN.md](PLAN.md) 为准。

| 模块 | 状态 |
|------|------|
| 后端实体建模 + EF Core 映射 | ✅ 已完成（基线 36 张表） |
| 后端 Controller / Service 层 | ✅ 已完成（认证、演出场次、座位、订单、退改签、核销、文件等） |
| 前端用户端页面（登录/注册、首页、搜索、演出详情、选座、订单、个人中心） | ✅ 已完成 |
| 前端管理端页面（看板、用户、演出、场次、订单、退改签、策略、核销、发布、座位图、营销） | ✅ 已完成 |
| 前端统一请求层 / 代理 | ✅ 已完成（openapi-fetch + vite proxy） |
| 数据库基线建表脚本 | ✅ 已完成（`db/baseline/merged_ddl.sql`，36 张表） |
| 数据库增量迁移 | ✅ 已完成（`db/migrations/`，含 UTC 时间统一，当前 38 张表） |
| 测试数据生成工具 | ✅ 已完成（`db/testdata/`，Bogus） |
| 后端单元测试 + 前端 E2E | ✅ 已完成（CI 自动执行） |

---

## 目录结构

```
showtime/
├── backend/                        # ASP.NET Core 后端
│   ├── Controllers/                # API 控制器（按模块分目录）
│   ├── Services/                   # 业务服务（订单/退改签/座位/文件/消息等）
│   ├── Data/
│   │   ├── AppDbContext.cs         # 数据库上下文（默认 Schema：APP_OWNER）
│   │   └── Configurations/         # EF Core Fluent API 映射（按模块划分）
│   ├── Entities/                   # 实体定义
│   ├── Common/                     # ApiResponse 信封、JWT、限流、审计、枚举等
│   ├── Program.cs                  # 应用入口（配置校验、依赖注入、中间件）
│   ├── ShowtimeBackend.csproj
│   ├── appsettings.json            # 公共配置
│   ├── appsettings.Development.json
│   └── appsettings.example.json    # 配置模板（真实值不入仓库）
├── backend.Tests/                  # 后端单元测试（xUnit，SQLite 内存库）
├── frontend/                       # React + TS + Vite 前端
│   ├── src/
│   │   ├── api/                    # openapi-fetch 客户端 + 类型（openapi.json）
│   │   ├── pages/                  # 用户端与管理端页面
│   │   ├── router/                 # 路由配置
│   │   ├── realtime/               # SignalR 实时通知
│   │   └── main.tsx
│   ├── tests/                      # Playwright E2E（mock API）
│   ├── package.json
│   ├── vite.config.ts              # 开发代理 /api、/files、/hubs
│   └── .env.example                # 前端环境变量模板
├── db/                             # 数据库脚本
│   ├── baseline/                   # 基线建表脚本（merged_ddl.sql 为可执行合并版，36 表）
│   ├── migrations/                 # 增量变更脚本（日期__模块描述.sql）
│   └── testdata/                   # 测试数据生成工具
├── doc/                            # 项目文档
│   ├── API.md                      # API 约定、错误码、数据模型、状态枚举
│   ├── API-CHANGELOG.md            # 接口实现变更日志 + 当前接口清单
│   ├── DEPLOYMENT.md               # 部署文档
│   ├── FAQ.md                      # 联调问题记录
│   ├── audit-design.md             # 审计 / 操作日志设计
│   └── 用户操作手册-*.md            # 普通用户 / 管理员操作手册
├── docker-compose.yml              # 本地一键起 Redis / RabbitMQ
├── CONVENTIONS.md                  # 项目约定（Git / 数据库 / 运行配置 / 编码）
├── PLAN.md                         # 项目开发计划
└── README.md
```

---

## 数据模型概览

数据库基线共 **36 张表**，分 4 大模块；应用 `db/migrations/` 全部增量后为 **38 张表**（新增 `DYNAMIC_PRICING_RULE`、`T_ORDER_EVENT_OUTBOX`）。完整字段与约束见 `db/baseline/merged_ddl.sql`，实体映射见 `backend/Data/Configurations/`。

| 模块 | 表数量 | 主要表 |
|------|:------:|--------|
| 用户与权限 | 10 | `SYS_USER`、`ROLE`、`PERMISSION`、`ORG_STRUCTURE`、`USER_REAL_NAME`、`USER_SESSION`、`OPERATION_LOG` 等 |
| 演出与场次 | 9 | `SHOW`、`CATEGORY`、`TAG`、`VENUE`、`SHOW_SESSION`、`PRICE_STRATEGY`、`PURCHASE_LIMIT`、`MARKETING_CONTENT` 等 |
| 座位区域 | 7 | `SEAT_MAP`、`SEAT_SECTION`、`SEAT`、`SEAT_LOCK`、`SEAT_RESERVATION`、`SEAT_RULE`、`SEAT_RULE_SCOPE` |
| 订单票务 | 10 | `T_ORDER`、`ORDER_ITEM`、`PAYMENT`、`E_TICKET`、`REFUND_REQUEST`、`EXCHANGE_REQUEST`、`REFUND_POLICY`、`EXCHANGE_POLICY` 等 |

---

## 快速开始（本地运行）

### 1. 环境准备

| 依赖 | 版本要求 | 用途 |
|------|----------|------|
| .NET SDK | 10.0 | 后端编译运行 |
| Node.js | ^20.19.0 或 >=22.12.0（建议 22 LTS） | 前端构建（Vite 8 要求） |
| Oracle 数据库 | 21c XE | 数据存储（连接 `120.27.157.163:1521/XEPDB1`） |
| Docker | 可选 | 本地起 Redis / RabbitMQ |

### 2. 启动后端

后端本地通过 **user-secrets** 注入 Oracle 连接串（不提交仓库），**不需要设置 `Oracle_UserId`/`Oracle_Password` 环境变量**：

```bash
# PowerShell
cd backend
dotnet user-secrets set "ConnectionStrings:Oracle" "User Id=你的个人账号;Password=你的密码;Data Source=120.27.157.163:1521/XEPDB1"

# 可选：本地起 Redis（选座锁加速；不起也不影响启动，自动降级纯 Oracle）
docker compose up -d redis

dotnet restore
dotnet run
```

默认监听 `http://localhost:5146`。开发环境可访问：

- 健康检查：`http://localhost:5146/`
- API 文档（Scalar）：`http://localhost:5146/scalar/v1`
- OpenAPI JSON：`http://localhost:5146/openapi/v1.json`

### 3. 启动前端

```bash
cd frontend
npm install
cp .env.example .env.development   # VITE_API_BASE_URL 留空即可
npm run dev
```

默认监听 `http://localhost:5173`。前端开发代理已配置（`/api`、`/files`、`/hubs`），默认指向生产后端 `http://120.27.157.163:5146`；本地全栈联调时在 `frontend/.env.development` 设置 `VITE_DEV_PROXY_TARGET=http://localhost:5146`（本地文件，勿提交）。

### 4. 准备测试数据

建表完成后，用 `db/testdata` 工具生成虚拟数据（演出、场次、座位、用户等）：

```bash
cd db/testdata
export IdentityData__EncryptionKey="<Base64-32-bytes>"   # 与后端一致的实名加密密钥
dotnet run -- "User Id=<姓名全拼>;Password=<密码>;Data Source=<host>:1521/XEPDB1"
```

详见 [db/testdata/README.md](db/testdata/README.md)。

### 5. 常用测试账号（testdata 生成后可用）

| 账号 | 密码 | 角色 |
|------|------|------|
| `admin` | `Admin@12345` | 管理员（可访问 `/admin`） |
| `testuser1` ~ `testuser3` | `Test@12345` | 普通用户 |
| `user1001` ~ `user1060` | `Test@12345` | 批量观众（演示数据） |

---

## 环境变量

本地开发所需最少配置通过 `dotnet user-secrets` 注入；**生产环境使用同名环境变量（`__` 分隔层级）注入**，完整清单见 [doc/DEPLOYMENT.md](doc/DEPLOYMENT.md) 与 `backend/appsettings.example.json`。

| 变量（生产） | 必填 | 说明 |
|--------|:----:|------|
| `ConnectionStrings__Oracle` | 是 | Oracle 连接串（本地用 user-secrets 注入同名键） |
| `ConnectionStrings__Redis` | 是 | Redis 连接串（生产缺失启动即报错） |
| `Jwt__Key` | 是 | JWT 签名密钥（≥32 字节，生产勿用开发密钥） |
| `TicketSecurity__SigningKeyBase64` | 是 | 电子票签名密钥（Base64，≥32 字节） |
| `IdentityData__EncryptionKey` | 是 | 实名加密密钥（Base64，恰好 32 字节） |
| `Oss__Enabled` 等 `Oss__*` | 使用 OSS 时 | 默认 true；不用 OSS 设 `false` |
| `ASPNETCORE_ENVIRONMENT` | 否 | 运行环境（`Development` / `Production`） |

---

## 数据库约定

- 基线共 **36 张表**，可执行脚本位于 `db/baseline/merged_ddl.sql`；增量脚本位于 `db/migrations/`，按 `日期__模块描述.sql` 命名，**新环境需在基线后按顺序执行全部迁移**（当前完整 Schema 38 张表）。
- Schema 由 **`APP_OWNER`** 持有；**DDL 变更必须经 `DEPLOY_USER` 执行**，个人账号仅能执行 DML。
- 个人账号可用 `ALTER SESSION SET CURRENT_SCHEMA = APP_OWNER;` 切换默认 Schema。
- 库内 TIMESTAMP 统一存 UTC（2026-09-09 起），后端输出统一带 `Z`。

完整规范见 [CONVENTIONS.md](CONVENTIONS.md)。

---

## 相关文档

| 文档 | 说明 |
|------|------|
| [doc/API.md](doc/API.md) | API 约定、错误码、数据模型、状态枚举 |
| [doc/API-CHANGELOG.md](doc/API-CHANGELOG.md) | 接口变更日志与当前接口清单 |
| [doc/DEPLOYMENT.md](doc/DEPLOYMENT.md) | 环境依赖、配置项、数据库初始化、部署流程、排查 |
| [doc/FAQ.md](doc/FAQ.md) | 联调问题记录与错误码速查 |
| [doc/audit-design.md](doc/audit-design.md) | 审计 / 操作日志设计 |
| [doc/用户操作手册-普通用户.md](doc/用户操作手册-普通用户.md) | 普通用户操作手册 |
| [doc/用户操作手册-管理员.md](doc/用户操作手册-管理员.md) | 管理员操作手册 |
| [PLAN.md](PLAN.md) | 项目开发计划与各角色任务 |
| [CONVENTIONS.md](CONVENTIONS.md) | Git 工作流、数据库规范、编码约定 |
| [db/testdata/README.md](db/testdata/README.md) | 测试数据生成工具说明 |

---

## 贡献规范

- **Git 工作流**：`main` / `Develop` / `Feature` 三类分支；`main` 与 `Develop` 需通过 PR 合并，且至少一位同学 Review。**任何人不得直接在 `main` 上提交**。
- **编码**：统一 **UTF-8（无 BOM）+ LF**，已通过 `.editorconfig` 与 `.gitattributes` 保证。
- 详细约定见 [CONVENTIONS.md](CONVENTIONS.md)。
