# 项目约定
> 以下约定视实际情况而变
1. git：
   - 采用git flow工作流，参考[这篇文章](https://blog.csdn.net/sunyctf/article/details/130587970)
   - 我们不需要Release分支和Hotfix分支，所有分支分为三类：`main`、`Develop`、`Feature`
     - `main`分支上永远保存最稳定的版本，“主要分支上的代码必须是合并自经过多轮测试及已经发布一段时间且线上稳定的预发分支”。**任何人不得直接在main上提交**。
     - `Develop`分支接收Feature分支的合并，用来存放最新的开发版，在我们这个场景里，算是main的缓冲区。
     - `Feature`分支（格式：`Feature/xxxx功能`），“用于开发即将发布版本或未来版本的新功能或者探索新功能”，直接分支于Develop，最终也合并入Develop，是进行开发的分支。
     - `main`和`Develop`分支必须通过提交PR来合并，**由至少一位的同学Review**后才能合并到Develop分支
   - git commit的信息英文、中文、混用都可以，但是请一定写详细
   - 编码：统一使用**UTF-8（没有BOM）+LF**的组合，这一点已经通过.editorconfig和.gitattributes尽可能做了保证，主流IDE应该会自动识别。
     - 大家的VS可能用的是GB2312+CRLF，一定记得统一配置
     - 使用UTF-8在Windows的cmd下可能出现乱码，大家可以查一下如何修改cmd的默认编码
2. 数据库：
   - 数据库Schema由无法登录的用户**APP_OWNER**持有
   - 所有对Schema的变更必须通过DEPLOY_USER这个用户
   - **日常开发时，使用自己的账号连接数据库**，账号密码都是你的姓名全拼，比如liborui的密码是liborui
     - 个人账号能创建自己的Schema，并对自己的Schema进行操作（可以复制一份APP_OWNER的Schema用于测试）。对于APP_OWNER用户持有的Schema，能够进行插入、删除等操作，但无法进行drop和alter，即只能执行DML，不能做DDL。
     - 个人账号通过`ALTER SESSION SET CURRENT_SCHEMA = APP_OWNER;`来修改当前默认的Schema为APP_OWNER持有的，之后进行select等操作就只会选中APP_OWNER的Schema了。
   - 如果要对数据库Schema进行修改，需要经过讨论->编写Alter脚本->保存在git下->由DEPLOY_USER执行。
   - 数据库信息
     - IP地址：120.27.157.163
     - 端口：1521
     - 数据库名称：XEPDB1
     - 版本：Oracle XE 21c
     - sql*plus示例：sqlplus liborui/liborui@//120.27.157.163:1521/XEPDB1
     - 当前共36张表，建表脚本在db/baseline下
3. Redis（选座锁分布式锁，里程碑 4 引入）：
   - 角色定位：Redis 只是**加速层**，`SEAT_LOCK` 表仍是锁的真相源与审计记录（DB 活动锁唯一索引 `UK_SEAT_LOCK_ACTIVE` 最终兜底并发）。Redis 不可用时选座锁**自动降级为纯 Oracle 流程**，购票不被阻断；想完全关掉 Redis 前置判定，把配置 `Redis:SeatLockGuardEnabled` 设为 `false` 即可（kill-switch，无需发版）。
   - 本地一键起 Redis：仓库根 `docker-compose.yml`：
     ```bash
     docker compose up -d redis
     docker compose ps        # showtime-redis 健康后即可
     ```
     Redis 连接串 `ConnectionStrings:Redis` 本地默认 `localhost:6379,abortConnect=false,connectRetry=3,connectTimeout=3000`，已内置在 `backend/appsettings.Development.json`，无需自己配；生产通过环境变量 `ConnectionStrings__Redis` 注入。
   - 锁 key 设计（统一 `showtime:` 前缀，冒号分层）：
     - 选座锁：`showtime:seatlock:{sessionId}:{seatId}`，value = `SEAT_LOCK.LockToken`（32 位 hex），TTL 与 DB 锁期均取 `Redis:SeatLockTtlSeconds`（默认 600 秒，锁期唯一配置源，改锁期只改这一处）
     - 排查孤儿 key / 清理环境用 `redis-cli --scan --pattern 'showtime:*'`，**不要在生产执行 `KEYS *` / `flushdb` / `flushall`**
   - 使用规则：
     - 释放锁必须携带 token 比对（`ISeatLockGuard.ReleaseAsync` 已封装），禁止按 key 直接 `DEL` 他人持有的锁
     - 新功能的 Redis 用法一律走 `ISeatLockGuard`（锁），key 统一 `showtime:` 前缀；常规缓存（`IDistributedCache`）待里程碑 5 落地时再注册 `AddStackExchangeRedisCache`（此前不注册，避免空占一套连接配置）
     - 锁的合法生命周期（ACTIVE→CONVERTED/RELEASED/EXPIRED）只在 DB 裁决；Redis key 只是加速版的存在性标记，双通道不一致时靠 TTL 自愈，不要手工改 Redis 里的锁
     - 单测不依赖真实 Redis（fake 注入），改动选座锁相关代码后 `dotnet test` 必须全绿
4. OSS（阿里云对象存储：演出海报/营销图/头像等图片资源统一存储，里程碑 5 引入）：
   - 角色定位：图片只存阿里云 OSS（Bucket **公共读**，URL 可直接用于 `<img>`）；**后端代理上传**——AccessKey 只存在后端，前端一律调 `POST /api/files/upload`（需认证）或 `FileUploader` 组件。业务表照旧存 URL 字符串（`Show.PosterUrl` 等 **VARCHAR2(500)，无需改字段**）。
   - Kill-switch `Oss:Enabled`（默认 true）：true 时上传走阿里云 OSS；false 时若本地磁盘存储 `LocalStorage:Enabled` 为 true（`backend/appsettings.Development.json` 默认开启），上传落本地磁盘并经静态托管 `/files` 读回（开发/联调中间态，多实例共享挂载卷即互通）；**两者皆关闭**时上传接口返回 `503 FILE_STORAGE_NOT_CONFIGURED`。本地无 OSS 环境不装 OSS 也能照常开发/跑单测/上线其他功能。
   - 敏感项：`Oss:AccessKeyId` / `Oss:AccessKeySecret` **不入库不入 git**；本地用 `dotnet user-secrets set "Oss:AccessKeyId" "<ram-key>"`（及 Secret），生产用环境变量 `Oss__AccessKeyId` / `Oss__AccessKeySecret`（或 KMS/Docker secret）注入，不要写进任何提交进仓库的文件。启用时启动即校验 Endpoint/Bucket/BaseUrl/AccessKey 非空（fail-fast）。
   - 对象键（统一 `showtime/` 前缀，顶层目录即业务类型，GUID 服务端生成防猜测）：
     - `showtime/{folder}/{yyyy}/{MM}/{guid}.{ext}`；folder 白名单：`show`（演出海报）/ `marketing`（营销内容图）/ `avatar`（用户头像）/ `tmp`（临时）
     - 原文件名仅用于推断扩展名，落盘键一律服务端生成 GUID，杜绝路径穿越/重名覆盖
   - 安全：扩展名白名单（默认 jpg/jpeg/png/webp/gif）+ Content-Type `image/*` 二次校验 + 大小上限（默认 5MB，超限返回 413 `FILE_TOO_LARGE`）+ 请求体 `RequestSizeLimit` 兜底，禁 .html/.svg/.js 等脚本类。
   - 资源准备（一次性控制台操作）：Bucket（如 `showtime-assets`，**公共读**，Region 与 Endpoint 一致）+ RAM 子账号最小权限（仅 `oss:PutObject`/`oss:DeleteObject`，Resource 限定 `acs:oss:*:*:showtime-assets/showtime/*`）+（可选）`showtime/tmp/` 生命周期 1 天清理。
   - 使用规则：新图片上传一律走统一上传接口/`FileUploader`，禁止前端手拼 URL；删除资源可选调 `IFileStorageService.DeleteObjectAsync` 清理对象；头像 URL 经 `PUT /api/users/me/avatar` 持久化到 `SYS_USER.AVATAR_URL`（DDL 走第 2 节 Alter 脚本流程）；单测不依赖真实 OSS（fake 注入 + 校验纯函数），改动相关代码后 `dotnet test` 必须全绿。
5. 其他：
   - 尽可能遵守项目时间线的规定，如果有困难可以大家讨论调整时间线。如果无法遵守也没有及时告知大家，会拖慢整个项目的进度。
   - 如果数据库有什么问题或者有什么需要，请及时说出来，大家讨论解决。
6. 运行/配置：
   - 数据库连接串 `ConnectionStrings:Oracle`（完整连接串，如 `User Id=liborui;Password=liborui;Data Source=120.27.157.163:1521/XEPDB1`）**不提交仓库**：
     - 本地开发：用 `dotnet user-secrets` 注入（Development 环境自动加载，只存在你本机 `~/.microsoft/usersecrets` 下）：
       ```bash
       cd backend
       dotnet user-secrets set "ConnectionStrings:Oracle" "User Id=你的姓名全拼;Password=你的密码;Data Source=120.27.157.163:1521/XEPDB1"
       ```
     - 生产环境：通过环境变量 `ConnectionStrings__Oracle`（或 KMS/Docker secret）注入，不要写进 `appsettings.json` 或任何提交进仓库的文件。
     - 新增成员参考 `backend/appsettings.example.json` 模板了解需要配置哪些项。
   - Redis 连接串 `ConnectionStrings:Redis`（本地默认已内置在 `backend/appsettings.Development.json`：`localhost:6379,abortConnect=false,connectRetry=3,connectTimeout=3000`，无需 user-secrets 配置）：
     - 本地开发：仓库根 `docker compose up -d redis` 一键起（起不来时详见第 3 节 Redis 的端口占用说明）
     - 生产环境：通过环境变量 `ConnectionStrings__Redis`（或 KMS/Docker secret）注入，不要写进 `appsettings.json` 或任何提交进仓库的文件
     - Redis 配置项 `Redis:SeatLockGuardEnabled`（选座锁前置判定开关，默认 true）与 `Redis:SeatLockTtlSeconds`（锁期唯一来源：DB 锁期与 Redis key TTL 均取自此值）见第 3 节；生产连接串缺失时启动即报错（非开发环境不再静默兜底 localhost）。
   - OSS 配置 `Oss:` 节（Endpoint/Bucket/BaseUrl 本地已内置在 `backend/appsettings.Development.json`，`Enabled` 默认 false）：
     - 本地联调 OSS：user-secrets 注入 AccessKey（`Oss:AccessKeyId` / `Oss:AccessKeySecret`），并把 `Oss:Enabled` 置 true；具体见第 4 节与 `backend/appsettings.example.json` 模板
     - 生产环境：通过环境变量 `Oss__Endpoint` / `Oss__Bucket` / `Oss__BaseUrl` / `Oss__AccessKeyId` / `Oss__AccessKeySecret` / `Oss__Enabled`（或 KMS/Docker secret）注入，AccessKey 严禁写进任何提交进仓库的文件
   - RabbitMQ 连接串 `ConnectionStrings:RabbitMq`（本地默认已内置在 `backend/appsettings.Development.json`：`amqp://guest:guest@localhost:5672/`）：
     - 默认关闭（`RabbitMq:Enabled=false`）：不连接 broker，订单/退款 outbox 由进程内 `LocalOrderEventPublisher` 完成退款与实时通知，单机开发无需 RabbitMQ；
     - 本地启用：仓库根 `docker compose up -d rabbitmq` 一键起（管理台 http://localhost:15672 ，guest/guest），再把 `RabbitMq:Enabled` 置 true（user-secrets `RabbitMq:Enabled` 或环境变量 `RabbitMq__Enabled=true`）
     - 生产环境：通过环境变量 `ConnectionStrings__RabbitMq` 注入
   - 前端环境变量：`frontend/.env.production`、`frontend/.env.development` 已移出版本控制，真实值由 CI/部署注入；仓库内只保留 `frontend/.env.example` 占位模板。
   - JWT 签名密钥 `Jwt:Key` 在 `backend/appsettings.Development.json` 中配置了一个**仅限本地开发（DEV-ONLY）**的随机密钥，保证本地可直接启动。该值仅用于 Development 环境，**生产环境严禁复用**，必须通过环境变量 `Jwt__Key`（或其他密钥管理机制）覆盖，切勿把开发密钥写入 `appsettings.json` 或生产配置。
7. 新人环境准备：
   - 前置条件：Git、**.NET 10 SDK**、**Node.js 22+**、**Docker**（可选，仅本地起 Redis 用）；个人数据库账号（账号密码均为姓名全拼，如 liborui/liborui），未开通找数据库管理员。编辑器编码保持 **UTF-8 无 BOM + LF**（Windows cmd 乱码用 `chcp 65001`）。
   - 拉代码：`git clone <仓库地址>` → `git checkout Develop` → `git checkout -b Feature/xxx功能`。
   - 后端：
     ```bash
     cd backend
     dotnet restore
     # 配置数据库连接串（唯一需要自己配的项，UserSecretsId 已内置，无需 init）
     dotnet user-secrets set "ConnectionStrings:Oracle" "User Id=姓名全拼;Password=密码;Data Source=120.27.157.163:1521/XEPDB1"

     # （可选）本地起 Redis，供选座锁 Redis 前置判定使用；不装也不影响启动和单测，锁座会自动降级为纯 Oracle
     docker compose up -d redis
     redis-cli ping    # 返回 PONG 即就绪（需本机安装 redis-cli）

     # （可选）联调 OSS 时才需要：注入 AccessKey（见第 4 节），并把 Development 里 Oss:Enabled 置 true；不配不影响启动/单测
     dotnet user-secrets set "Oss:AccessKeyId" "<ram-access-key-id>"
     dotnet user-secrets set "Oss:AccessKeySecret" "<ram-access-key-secret>"

     dotnet run        # 监听 http://localhost:5146
     ```
     - Jwt:Key 不用配，appsettings.Development.json 已有 DEV-ONLY 本地密钥
     - 验证：浏览器打开 `http://localhost:5146/openapi/v1.json`
     - 单测不需要数据库（SQLite 内存库）：`dotnet test` 应全部通过
   - 前端：
     ```bash
     cd frontend
     npm install
     cp .env.example .env.development   # VITE_API_BASE_URL 留空即可
     npm run dev                        # vite dev server 5173
     ```
   - 数据库联通性验证（建议先做再配连接串）：
     ```bash
     sqlplus 姓名全拼/密码@//120.27.157.163:1521/XEPDB1
     ALTER SESSION SET CURRENT_SCHEMA = APP_OWNER;   # 切到共享 Schema
     ```
   - 常见坑：
     - 不配连接串后端也能启动，但第一个查库请求会报 `Connection string 'Oracle' is not set.`，不是代码 bug，是没配 user-secrets
     - 前端 vite proxy 默认指向生产后端（`frontend/vite.config.ts` 中 `devProxyTarget`，可用环境变量 `VITE_DEV_PROXY_TARGET` 覆盖，如 `VITE_DEV_PROXY_TARGET=http://localhost:5146` 连本地后端，勿提交该值），开箱即用会直连生产环境、操作生产数据，慎用；
     - E2E（`frontend/tests/`）通过 `page.route` 全量 mock `/api/**`，不连接任何真实后端，CI 与本地运行均不产生生产数据
     - 不要用 `VITE_API_BASE_URL=http://localhost:5146` 直连本地后端：后端未配置 CORS，浏览器会拦截跨域请求
     - `dotnet user-secrets` 与 `frontend/.env.development` 都是本地文件，提交前 `git status` 确认没把它们带进 commit
     - `docker compose up -d redis` 报 `bind ... 6379: address already in use`：说明本机已有 Redis 在运行（端口被占），直接用现有的即可，不用重复起容器
     - Redis 没起/挂了不影响后端启动和 `dotnet test`（懒连接 + 自动降级），只是选座锁会退化为纯 Oracle 流程；不要在共享/生产 Redis 上执行 `flushdb`/`flushall`，排查孤儿 key 用 `redis-cli --scan --pattern 'showtime:*'`
