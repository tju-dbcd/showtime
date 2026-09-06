# SHOWTIME —— 演出活动票务管理平台

本项目是一个演出售票系统，主要用于实现演出活动的票务管理与在线购票功能。系统围绕用户与权限管理、演出信息发布、场次管理、座位安排、订单处理、支付核销等核心业务展开，旨在为用户提供便捷的购票体验，同时协助管理方实现票务数据的统一管理与实时统计分析。

---

## 技术栈

| 层次 | 技术 | 说明 |
|------|------|------|
| 后端框架 | ASP.NET Core（.NET 10）+ C# | Web API |
| ORM | Entity Framework Core | 使用 `Oracle.EntityFrameworkCore` 驱动 |
| 数据库 | Oracle XE 21c | 36 张表，Schema 归 `APP_OWNER` |
| API 文档 | OpenAPI + Scalar | 开发环境自动生成 |
| 前端 | React 19 + TypeScript + Vite | SPA |
| UI 组件库 | Ant Design 6 | — |
| 前端路由 | React Router 7 | — |
| HTTP 客户端 | Axios | — |
| 代码检查 | Oxlint（前端） | — |

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
| 后端实体建模 + EF Core 映射 | ✅ 已完成（4 大模块，36 张表） |
| 后端 Controller / Service 层 | 🚧 开发中（尚未实现） |
| 前端路由骨架 + 页面占位 | ✅ 已完成（3 个路由页） |
| 前端统一请求层 / 代理 | 🚧 待实现 |
| 数据库基线建表脚本 | ✅ 已完成（`db/baseline/merged_ddl.sql`） |
| 测试数据生成工具 | ✅ 已完成（`db/testdata/`，Bogus） |

---

## 目录结构

```
showtime/
├── backend/                        # ASP.NET Core 后端
│   ├── Data/
│   │   ├── AppDbContext.cs         # 数据库上下文（默认 Schema：APP_OWNER）
│   │   └── Configurations/         # EF Core Fluent API 映射（按模块划分）
│   │       ├── OrderTicket/        # 订单票务模块
│   │       ├── SeatZone/           # 座位区域模块
│   │       ├── ShowSession/        # 演出场次模块
│   │       └── UserPermission/     # 用户权限模块
│   ├── Entities/                   # 实体定义（对应 36 张表）
│   │   ├── Base/                   # 审计字段基类 AuditableEntity
│   │   ├── OrderTicket/
│   │   ├── SeatZone/
│   │   ├── ShowSession/
│   │   └── UserPermission/
│   ├── Program.cs                  # 应用入口（连接串、OpenAPI 注册）
│   ├── ShowtimeBackend.csproj
│   ├── appsettings.json
│   └── appsettings.Development.json
├── frontend/                       # React + TS + Vite 前端
│   ├── src/
│   │   ├── pages/                  # Home / Order / PerformanceDetail
│   │   ├── router/                 # 路由配置
│   │   ├── App.tsx
│   │   └── main.tsx
│   ├── package.json
│   ├── vite.config.ts
│   └── tsconfig*.json
├── db/                             # 数据库脚本
│   ├── baseline/                   # 基线建表脚本（merged_ddl.sql 为合并版）
│   ├── migrations/                 # 增量变更脚本（日期__模块描述.sql）
│   └── testdata/                   # 测试数据生成工具
├── docs/                           # 项目文档
│   ├── API.md                      # API 约定与规划
│   └── DEPLOYMENT.md               # 部署文档
├── CONVENTIONS.md                  # 项目约定（Git / 数据库 / 编码）
├── PLAN.md                         # 项目开发计划
└── README.md
```

---

## 数据模型概览

共 **36 张表**，分 4 大模块。完整字段与约束见 `db/baseline/merged_ddl.sql`，实体映射见 `backend/Data/Configurations/`。

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
| Node.js | 18+（建议 20 LTS） | 前端构建 |
| Oracle 数据库 | 21c XE | 数据存储（连接 `120.27.157.163:1521/XEPDB1`） |

### 2. 启动后端

后端启动时从环境变量读取数据库账号密码，**必须先配置环境变量**：

```bash
# PowerShell
$env:Oracle_UserId = "你的个人账号"
$env:Oracle_Password = "你的密码"

# CMD
set Oracle_UserId=你的个人账号
set Oracle_Password=你的密码
```

```bash
cd backend
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
npm run dev
```

默认监听 `http://localhost:5173`。前端接入后端时需在 `vite.config.ts` 配置代理（见 [部署文档](docs/DEPLOYMENT.md)）。

### 4. 准备测试数据

建表完成后，用 `db/testdata` 工具生成虚拟数据（演出、场次、座位、用户等）：

```bash
cd db/testdata
dotnet run
```

详见 [db/testdata/README.md](db/testdata/README.md)。

---

## 环境变量

| 变量名 | 必填 | 说明 |
|--------|:----:|------|
| `Oracle_UserId` | 是 | 数据库连接用户名 |
| `Oracle_Password` | 是 | 数据库连接密码 |
| `ASPNETCORE_ENVIRONMENT` | 否 | 运行环境（`Development` / `Production`） |

---

## 数据库约定

- 共 **36 张表**，建表脚本位于 `db/baseline/`，其中 `merged_ddl.sql` 为可直接执行的完整合并版。
- 增量变更脚本位于 `db/migrations/`，按 `日期__模块描述.sql` 命名。
- Schema 由 **`APP_OWNER`** 持有；**DDL 变更必须经 `DEPLOY_USER` 执行**，个人账号仅能执行 DML。
- 个人账号可用 `ALTER SESSION SET CURRENT_SCHEMA = APP_OWNER;` 切换默认 Schema。

完整规范见 [CONVENTIONS.md](CONVENTIONS.md)。

---

## 相关文档

| 文档 | 说明 |
|------|------|
| [doc/API.md](doc/API.md) | API 约定、错误码、数据模型、状态枚举、规划接口 |
| [doc/DEPLOYMENT.md](doc/DEPLOYMENT.md) | 环境依赖、配置项、数据库初始化、部署流程、排查 |
| [PLAN.md](PLAN.md) | 项目开发计划与各角色任务 |
| [CONVENTIONS.md](CONVENTIONS.md) | Git 工作流、数据库规范、编码约定 |
| [db/testdata/README.md](db/testdata/README.md) | 测试数据生成工具说明 |

## 实名信息 API 与本地密钥

用户实名记录接口均需要 JWT：

- `GET /api/users/me/real-names`
- `POST /api/users/me/real-names`
- `PUT /api/users/me/real-names/{realNameId}`
- `PATCH /api/users/me/real-names/{realNameId}/default`
- `DELETE /api/users/me/real-names/{realNameId}`

接口只返回脱敏身份证号。数据库 `USER_REAL_NAME.ID_CARD_NO` 使用 AES-256-GCM
加密，运行后端前必须配置 `IdentityData:EncryptionKey`（环境变量写法为
`IdentityData__EncryptionKey`），其值是 Base64 编码的 32 个随机字节。可以在
PowerShell 中生成：

```powershell
[Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
```

本地推荐写入 user-secrets，严禁提交真实密钥：

```powershell
dotnet user-secrets --project backend set "IdentityData:EncryptionKey" "<Base64密钥>"
```

当前版本的实名认证为开发阶段模拟实现：身份证格式校验通过即标记为已认证。
共享数据库若已有历史明文，先阅读
`db/tools/IdentityDataMigration/README.md`，dry-run 后再显式迁移。

---

## 贡献规范

- **Git 工作流**：`main` / `Develop` / `Feature` 三类分支；`main` 与 `Develop` 需通过 PR 合并，且至少一位同学 Review。**任何人不得直接在 `main` 上提交**。
- **编码**：统一 **UTF-8（无 BOM）+ LF**，已通过 `.editorconfig` 与 `.gitattributes` 保证。
- 详细约定见 [CONVENTIONS.md](CONVENTIONS.md)。
