# SHOWTIME 部署文档

本文档说明 SHOWTIME 项目从本地开发到生产部署的完整流程，包含环境依赖、配置项清单、数据库初始化、后端与前端部署、反向代理及常见问题排查。

> 生产部署部分（第 5 节）为**通用骨架**，具体服务器信息（操作系统、域名、进程托管方式）确认后请据此补充完善。
> 配置注入方式请以 `backend/appsettings.example.json` 为模板：**真实连接串/密钥一律不入仓库**，本地开发用 `dotnet user-secrets`，生产用环境变量或 Secret 管理注入。

---

## 目录

1. [环境依赖](#1-环境依赖)
2. [配置项清单](#2-配置项清单)
3. [数据库初始化](#3-数据库初始化)
4. [本地开发部署](#4-本地开发部署)
5. [生产部署骨架](#5-生产部署骨架)
6. [常见问题排查](#6-常见问题排查)

---

## 1. 环境依赖

| 软件 | 版本要求 | 用途 | 说明 |
|------|----------|------|------|
| .NET SDK | **10.0** | 后端编译运行 | 对应 `net10.0` TargetFramework；CI 使用 `10.0.x` |
| Node.js | **`^20.19.0` 或 `>=22.12.0`**（建议 22 LTS） | 前端构建 | Vite 8 的 engines 要求 |
| npm | 随 Node 附带 | 前端依赖管理 | 锁文件为 `package-lock.json` |
| Oracle 数据库 | **21c XE** | 数据存储 | 项目连接：`120.27.157.163:1521/XEPDB1`；Schema 归 `APP_OWNER` |
| Git | 2.x | 版本控制 | 遵循 [CONVENTIONS.md](../CONVENTIONS.md) 的分支规范 |
| Docker | 20+（可选） | 本地一键起 Redis / RabbitMQ | 使用仓库根 `docker-compose.yml` |

**可选基础设施**（均不阻塞后端启动，除非在非开发环境显式启用了对应功能）：

| 软件 | 用途 | 现状 |
|------|------|------|
| Redis 7 | 选座锁前置判定（防超卖加速层） | 仓库已支持；本地 `docker compose up -d redis`；Redis 不可用时自动降级为纯 Oracle 流程，**但生产环境缺失连接串时后端启动即报错** |
| RabbitMQ 3 | 订单/退款事件异步通知 | 仓库已支持，默认 `RabbitMq:Enabled=false` 不连 broker；需要时 `docker compose up -d rabbitmq` 并把开关置 true |
| 阿里云 OSS | 海报/营销图/头像等图片存储 | 仓库已支持（`Oss:Enabled` 默认 true）；也可用本地磁盘存储（`LocalStorage:Enabled=true`）替代 |
| Loki + Grafana | 日志监控看板 | 规划中（Serilog 已就绪，看板尚未在仓库落地） |

---

## 2. 配置项清单

### 2.1 配置来源与覆盖顺序

后端配置来自（后者覆盖前者）：`appsettings.json` → `appsettings.{ASPNETCORE_ENVIRONMENT}.json` → 环境变量 / `dotnet user-secrets`。

- **本地开发**：`backend/appsettings.Development.json` 已内置 Redis 连接串、DEV-ONLY JWT 密钥、`TicketSecurity` 密钥、本地磁盘存储开关等，个人只需用 `dotnet user-secrets` 注入 Oracle 连接串。
- **生产环境**：通过环境变量（systemd `EnvironmentFile`、CI/CD 变量、K8s Secret 等）注入，**不要写死在 `appsettings.json` 或代码中**。
- 完整配置模板见 `backend/appsettings.example.json`，占位符 `<...>` 必须替换为真实值。

### 2.2 后端必须配置项（生产环境）

以下变量缺失/不合法时，后端**启动即抛异常（fail-fast）**：

| 环境变量 | 必填 | 说明 | 示例 |
|----------|:----:|------|------|
| `ConnectionStrings__Oracle` | 是 | 完整 Oracle 连接串。缺失时第一个 DB 请求（含后台 outbox Worker 扫描）报 `Connection string 'Oracle' is not set.` | `User Id=liborui;Password=xxx;Data Source=120.27.157.163:1521/XEPDB1` |
| `ConnectionStrings__Redis` | 是 | Redis 连接串。非开发环境缺失即启动报错，不允许静默连 localhost | `localhost:6379,abortConnect=false,connectRetry=3,connectTimeout=3000` |
| `Jwt__Key` | 是 | JWT 签名密钥，**至少 32 个 UTF-8 字节**的随机值；严禁复用 `appsettings.Development.json` 中的 DEV-ONLY 密钥 | 随机 32+ 字节字符串 |
| `TicketSecurity__SigningKeyBase64` | 是 | 电子票二维码 HMAC 密钥，Base64 解码后至少 32 字节 | `dGVzdC1rZXktZm9yLWNpLW9ubHktZG8tbm90LXVzZS1pbi1wcm9kdWN0aW9u` |
| `IdentityData__EncryptionKey` | 是 | 实名身份证 AES-256-GCM 密钥，Base64 解码后**恰好 32 字节**；密钥丢失后存量实名数据无法解密 | 32 字节随机值 Base64 |
| `Oss__Enabled` | 是（建议显式） | OSS 开关，**默认 `true`**。为 true 时必须同时提供下面 5 项；不使用 OSS 必须显式设 `false` | `true` / `false` |
| `Oss__Endpoint` / `Oss__Bucket` / `Oss__BaseUrl` | `Oss__Enabled=true` 时 | Endpoint 须与 Bucket 同 Region；BaseUrl 为公共读访问域名 | `https://oss-cn-hangzhou.aliyuncs.com` 等 |
| `Oss__AccessKeyId` / `Oss__AccessKeySecret` | `Oss__Enabled=true` 时 | RAM 子账号最小权限（Put/Delete），严禁入库/入 git | — |
| `ASPNETCORE_ENVIRONMENT` | 是 | 运行环境；生产设为 `Production`（此时 `/scalar`、`/openapi` 不再映射） | `Production` |
| `ASPNETCORE_URLS` | 推荐 | Kestrel 监听地址（systemd 场景不读 launchSettings） | `http://127.0.0.1:5146` |

**可选配置**：

| 环境变量 | 说明 |
|----------|------|
| `RabbitMq__Enabled=true` | 启用 RabbitMQ 事件发布；需同时注入 `ConnectionStrings__RabbitMq`（AMQP 连接串），默认 `false` 走进程内 outbox 处理 |
| `ForwardedHeaders__KnownNetworks__0` / `ForwardedHeaders__KnownProxies__0` | 反向代理不在本机（如 Docker/其他网段）时声明可信代理来源，见第 5.3 节 |
| `LocalStorage:Enabled` / `LocalStorage:RootDirectory` / `LocalStorage:BaseUrl` | 本地磁盘文件存储（多实例共享挂载目录），与 OSS 二选一 |
| `Redis__SeatLockGuardEnabled=false` | kill-switch：完全关闭 Redis 选座锁前置判定，走纯 Oracle 流程 |
| `Redis__SeatLockTtlSeconds` | 锁期唯一来源（默认 600 秒），DB 锁期与 Redis TTL 都取此值 |

> `OssOptions` 默认 `Enabled=true`：若生产 `appsettings.json` 中没有 OSS 配置节，务必用 `Oss__Enabled=false` 显式关闭，否则启动校验会因缺少 Endpoint 等而报错。

### 2.3 后端连接信息（代码固定值）

| 项 | 值 |
|----|----|
| 默认数据库 | `120.27.157.163:1521/XEPDB1`（Oracle XE 21c，Schema `APP_OWNER`） |
| 开发监听 | `http://localhost:5146`（`Properties/launchSettings.json`，另有 `https://localhost:7186` profile） |
| 默认 Schema | `APP_OWNER`（`AppDbContext.HasDefaultSchema`） |

### 2.4 前端配置

- 请求层使用 `openapi-fetch`，`baseUrl` 取 `VITE_API_BASE_URL`；**留空 = 同域**（走 vite 代理 / nginx 反代），非空 = 跨域直连后端（后端未配置 CORS，不建议）。
- 开发代理已配置在 `frontend/vite.config.ts`：`/api`、`/files`、`/hubs`（SignalR，含 WebSocket）转发到 `devProxyTarget`；默认指向生产后端 `http://120.27.157.163:5146`，本地全栈联调用环境变量覆盖：

  ```bash
  # frontend/.env.development（本地文件，勿提交）
  VITE_DEV_PROXY_TARGET=http://localhost:5146
  # VITE_API_BASE_URL 留空即可
  ```

- 环境变量模板见 `frontend/.env.example`（`VITE_API_BASE_URL`、`VITE_DEV_PROXY_TARGET`）。

---

## 3. 数据库初始化

### 3.1 账号与权限

遵循 [CONVENTIONS.md](../CONVENTIONS.md)：

- Schema 由 `APP_OWNER` 持有。
- **所有 DDL 变更必须由 `DEPLOY_USER` 执行**（连接后建议先 `ALTER SESSION SET CURRENT_SCHEMA = APP_OWNER;`，使后续未加 Schema 前缀的语句作用到 `APP_OWNER`）。
- 日常开发使用个人账号（账号密码均为姓名全拼），只能执行 DML；个人账号可用 `ALTER SESSION SET CURRENT_SCHEMA = APP_OWNER;` 切到共享 Schema。

### 3.2 首次建库（新环境）

**基线脚本（36 张表）+ 全部增量迁移 = 当前完整 Schema（38 张表）**：

1. 在**仓库根目录**启动 sqlplus（下面 `@` 脚本路径相对于仓库根目录），以 `DEPLOY_USER` 登录并切换到 `APP_OWNER` schema：

   ```bash
   sqlplus DEPLOY_USER/密码@//120.27.157.163:1521/XEPDB1
   SQL> ALTER SESSION SET CURRENT_SCHEMA = APP_OWNER;
   ```

2. 执行合并版基线（包含 36 张表、序列、触发器、索引；**推荐只执行该文件**，`db/baseline/` 下四个模块脚本为早期拆分稿，部分表名/结构与合并版不一致，不要混用）：

   ```sql
   SQL> @db/baseline/merged_ddl.sql
   ```

3. 按日期顺序执行全部增量迁移（括号内说明以 `DEPLOY_USER` 执行为前提；后几个脚本会自行校验/切换 schema，较早的脚本依赖第 1 步已切好的 `CURRENT_SCHEMA`）：

   ```sql
   SQL> @db/migrations/20260726__order_ticket_change.sql        -- 订单模块调整
   SQL> @db/migrations/20260812_showsession_change.sql          -- 场次查询索引
   SQL> @db/migrations/20260824__dynamic_pricing_rule.sql       -- 新增 DYNAMIC_PRICING_RULE
   SQL> @db/migrations/20260824__refund_workflow_support.sql    -- 退票工作流字段
   SQL> @db/migrations/20260827__sys_user_avatar_url.sql        -- SYS_USER.AVATAR_URL
   SQL> @db/migrations/20260830__exchange_workflow_support.sql  -- 改签工作流字段
   SQL> @db/migrations/20260904__order_idempotency.sql          -- 幂等键（函数唯一索引）
   SQL> @db/migrations/20260905__order_event_outbox.sql         -- 新增 T_ORDER_EVENT_OUTBOX
   SQL> @db/migrations/20260909__utc_timestamps.sql             -- 时间默认值改 UTC
   SQL> @db/migrations/20260909__utc_triggers_explicit.sql      -- 触发器时间赋值改 UTC
   ```

4. 校验表数量与触发器状态（应以 `APP_OWNER` 为准，DEPLOY_USER 自身一般没有业务表）：

   ```sql
   SQL> SELECT COUNT(*) FROM all_tables WHERE owner = 'APP_OWNER';
   SQL> SELECT trigger_name, status FROM all_triggers WHERE owner = 'APP_OWNER' ORDER BY 1;
   ```

> 若目标库已由历史脚本初始化（如团队共享库），**不要重复执行基线**，只需核对并补跑缺失的增量迁移。

### 3.3 增量变更（日常）

历史增量脚本位于 `db/migrations/`，按 `日期__模块描述.sql` 命名（同日期多个脚本按文件名排序执行）。新增变更时，约定流程为：**讨论 → 编写 Alter 脚本 → 存入 `db/migrations/` → 由 `DEPLOY_USER` 执行**。

### 3.4 测试数据

用 `db/testdata` 下的 Bogus 生成器灌入虚拟数据（覆盖当前完整 38 张表 schema）：

```bash
cd db/testdata
# 与后端一致的实名加密密钥（32 字节 Base64），缺失会拒绝执行
export IdentityData__EncryptionKey="<Base64-32-bytes>"
dotnet run -- "User Id=<姓名全拼>;Password=<密码>;Data Source=<host>:1521/XEPDB1"
```

也可以不传连接串参数，改用 `db/testdata/appsettings.json` 中 `ConnectionStrings:DefaultConnection`。生成参数与测试账号见 [db/testdata/README.md](../db/testdata/README.md)。

---

## 4. 本地开发部署

### 4.1 后端

```bash
# 1.（必须）配置 Oracle 连接串，存入本机 user-secrets（Development 环境自动加载，不入仓库）
cd backend
dotnet user-secrets set "ConnectionStrings:Oracle" "User Id=你的姓名全拼;Password=你的密码;Data Source=120.27.157.163:1521/XEPDB1"

# 2.（可选）起 Redis，供选座锁 Redis 前置判定；不起也不影响启动（自动降级纯 Oracle）
docker compose up -d redis

# 3.（可选）联调实名加密时注入 32 字节 Base64 密钥；不配可启动，但新增/读取实名会报错
# dotnet user-secrets set "IdentityData:EncryptionKey" "<Base64-32-bytes>"

# 4. 还原并运行
dotnet restore
dotnet run
```

默认地址：`http://localhost:5146`（HTTPS profile 为 `https://localhost:7186`，见 `backend/Properties/launchSettings.json`）。

验证（仅 Development/Testing 环境映射 OpenAPI/Scalar）：

```bash
curl http://localhost:5146/            # 返回 "Showtime API is running."
curl http://localhost:5146/openapi/v1.json   # OpenAPI JSON
# 浏览器打开 http://localhost:5146/scalar/v1 查看 Scalar 文档
```

### 4.2 前端

```bash
cd frontend
npm install
cp .env.example .env.development   # VITE_API_BASE_URL 留空；连本地后端时设 VITE_DEV_PROXY_TARGET=http://localhost:5146（本地文件，勿提交）
npm run dev       # 启动开发服务器（默认 http://localhost:5173）
```

常用脚本：

| 命令 | 说明 |
|------|------|
| `npm run dev` | 启动开发服务器（HMR） |
| `npm run build` | 类型检查 + 生产构建（产物在 `dist/`） |
| `npm run preview` | 本地预览生产构建 |
| `npm run lint` | Oxlint 代码检查 |
| `npx playwright test` | E2E 测试（`page.route` 全量 mock `/api/**`，不连真实后端） |

### 4.3 单测

后端单测使用 SQLite 内存库 + fake 注入，不依赖真实 Oracle/Redis/OSS：

```bash
cd backend
dotnet test
```

---

## 5. 生产部署骨架

> 以下为通用推荐方案，具体服务器环境确认后替换相应占位符。

### 5.1 后端部署

推荐 Linux 服务器 + `systemd` 托管：

1. **发布**：在具备 .NET SDK 的机器上构建框架依赖发布包：

   ```bash
   cd backend
   dotnet publish -c Release -o ./publish
   ```

2. **上传**至服务器（如 `/opt/showtime/backend`）。

3. **创建 systemd 单元** `/etc/systemd/system/showtime-backend.service`：

   ```ini
   [Unit]
   Description=Showtime Backend
   After=network.target

   [Service]
   WorkingDirectory=/opt/showtime/backend
   ExecStart=/usr/bin/dotnet /opt/showtime/backend/ShowtimeBackend.dll
   EnvironmentFile=/etc/showtime/backend.env
   Restart=always
   RestartSec=5

   [Install]
   WantedBy=multi-user.target
   ```

4. **环境变量文件** `/etc/showtime/backend.env`（必填项见 2.2，以下为最小示例；`Oss:Enabled` 按是否使用 OSS 二选一）：

   ```ini
   ASPNETCORE_ENVIRONMENT=Production
   ASPNETCORE_URLS=http://127.0.0.1:5146

   ConnectionStrings__Oracle=User Id=<服务账号>;Password=<密码>;Data Source=<host>:1521/XEPDB1
   ConnectionStrings__Redis=localhost:6379,abortConnect=false,connectRetry=3,connectTimeout=3000

   Jwt__Key=<随机且至少32字节的密钥，勿用开发密钥>
   TicketSecurity__SigningKeyBase64=<Base64解码后≥32字节>
   IdentityData__EncryptionKey=<Base64解码后恰好32字节>

   # 使用 OSS：Oss__Enabled=true + 下面 5 项
   Oss__Enabled=true
   Oss__Endpoint=https://oss-cn-hangzhou.aliyuncs.com
   Oss__Bucket=showtime-assets
   Oss__BaseUrl=https://showtime-assets.oss-cn-hangzhou.aliyuncs.com
   Oss__AccessKeyId=<ram-access-key-id>
   Oss__AccessKeySecret=<ram-access-key-secret>

   # 不使用 OSS（例如本地磁盘存储）：
   # Oss__Enabled=false
   # LocalStorage__Enabled=true

   # 启用 RabbitMQ 时再放开：
   # RabbitMq__Enabled=true
   # ConnectionStrings__RabbitMq=amqp://<user>:<pass>@<host>:5672/<vhost>
   ```

5. 启动并自启：

   ```bash
   sudo systemctl daemon-reload
   sudo systemctl enable --now showtime-backend
   ```

### 5.2 前端部署

1. **构建**（`VITE_API_BASE_URL` 留空，走同域反代）：

   ```bash
   cd frontend
   npm ci
   npm run build   # 产物在 frontend/dist/
   ```

2. 将 `dist/` 内容上传至 Web 服务器目录（如 `/var/www/showtime`）。

3. 由反向代理托管静态资源，并把 `/api`、`/hubs`（SignalR WebSocket）、`/files`（若启用本地磁盘存储）转发到后端。

### 5.3 Nginx 反向代理（示例）

```nginx
server {
    listen 80;
    server_name example.com;          # 替换为实际域名/IP

    root /var/www/showtime;
    index index.html;

    # 前端静态资源 + 前端路由（History 模式回退）
    location / {
        try_files $uri $uri/ /index.html;
    }

    # 后端 API 反向代理
    location /api/ {
        proxy_pass http://127.0.0.1:5146;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
    }

    # SignalR 实时通知（WebSocket 升级）
    location /hubs/ {
        proxy_pass http://127.0.0.1:5146;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection "upgrade";
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
    }

    # 本地磁盘文件存储静态托管（仅 LocalStorage:Enabled=true 时需要）
    location /files/ {
        proxy_pass http://127.0.0.1:5146;
        proxy_set_header Host $host;
    }
}
```

> HTTPS 建议通过 `certbot` 等工具配置 Let's Encrypt 证书。
>
> **注意**：Scalar / OpenAPI 只在 `Development` / `Testing` 环境映射（见 `Program.cs`），生产环境**不要**配置 `/scalar`、`/openapi` 转发，否则返回 404。需要查看接口文档时在开发环境访问或使用提交在仓库的 `frontend/src/api/openapi.json` 快照。

#### 后端 ForwardedHeaders 信任策略

后端已启用 `UseForwardedHeaders`（仅信任 `X-Forwarded-For` / `X-Forwarded-Proto`，`ForwardLimit = 1`），默认**只信任本机回环地址**（`127.0.0.0/8`、`::1/128`，即 Nginx 与后端同宿主机 / 同机部署），因此上面的 `proxy_pass http://127.0.0.1:5146` 示例开箱即用，异地登录检测与限流会按真实客户端 IP 工作。

- 若 Nginx 在容器/其他网段转发到后端，必须显式声明代理来源，否则 `X-Forwarded-For` 不会被采信（不会误信伪造头，但会退回代理 IP）：
  ```bash
  # 环境变量注入（示例：Docker bridge 172.16.0.0/12，按实际网段调整）
  ForwardedHeaders__KnownNetworks__0=172.16.0.0/12
  # 或指定单台代理地址：
  ForwardedHeaders__KnownProxies__0=10.0.0.5
  ```
- **不要把后端端口（如 5146）直接暴露公网**，否则任何人都能直接绕过 Nginx 访问后端（此时 `KnownNetworks` 也不会影响直连来源的判定）。
- 安全提示：不要信任整个公网网段；只填部署里真正的那台/那几台 Nginx 的地址。

---

## 6. 常见问题排查

| 现象 | 可能原因 | 处理 |
|------|----------|------|
| 后端启动报 `Connection string 'Oracle' is not set.` | 未配置 Oracle 连接串（生产缺少 `ConnectionStrings__Oracle`，或本地没做 user-secrets） | 配置后重启；本地命令见 4.1 |
| 后端启动报 `Connection string 'Redis' is not set.` | 非开发环境缺少 Redis 连接串 | 设置 `ConnectionStrings__Redis`（生产 Redis 为必填） |
| 后端启动报 `Jwt:Key ...` / `TicketSecurity:SigningKeyBase64 is required.` / `IdentityData:EncryptionKey is required.` | 生产缺少相应密钥配置 | 按 2.2 注入环境变量；严禁复用 Development 里的 DEV-ONLY 值 |
| 后端启动报 `Oss:Endpoint is required when Oss:Enabled is true.` | 未显式关闭 OSS 也未配置 OSS 项（Oss 默认 Enabled=true） | 不使用 OSS 时设 `Oss__Enabled=false`；使用则补齐 Endpoint/Bucket/BaseUrl/AccessKey |
| 连接 Oracle 报 `ORA-12154` / `ORA-12541` | 主机/端口/服务名错误或网络不通 | 核对 `120.27.157.163:1521/XEPDB1`，确认防火墙放行 |
| `ORA-01950` 无权限 / `ORA-01031` | 个人账号无权 DDL | DDL 交由 `DEPLOY_USER` 执行，个人账号仅 DML |
| 迁移脚本建到自己的 schema 下 | 执行前未切到 `APP_OWNER` | 用 `DEPLOY_USER` 连接后先执行 `ALTER SESSION SET CURRENT_SCHEMA = APP_OWNER;` |
| 建表后查询不到表 | Schema 不一致 | 确认当前 Schema 为 `APP_OWNER`，必要时 `ALTER SESSION SET CURRENT_SCHEMA = APP_OWNER` |
| 前端请求后端跨域失败 | 直接用 `VITE_API_BASE_URL` 跨域直连 | 开发环境走 vite proxy（`VITE_DEV_PROXY_TARGET`），生产用 Nginx 同域反代（后端未配置 CORS） |
| 生产访问 `/scalar/v1`、`/openapi/v1.json` 返回 404 | OpenAPI/Scalar 仅在 Development/Testing 映射 | 用开发环境查看，或参考仓库内的 OpenAPI 快照 `frontend/src/api/openapi.json` |
| 前端默认连到生产后端、操作到生产数据 | `vite.config.ts` 的 `devProxyTarget` 默认指向生产 | 本地联调设置 `VITE_DEV_PROXY_TARGET=http://localhost:5146`（勿提交） |
| `dotnet` 找不到 10.0 | SDK 版本不符 | 安装 .NET SDK 10.0 |
| `npm install`/`npm run dev` 报 Node 版本不兼容 | Node 版本低于 Vite 8 要求 | 升级到 Node `^20.19.0` 或 `>=22.12.0` |
