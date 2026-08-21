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
3. 其他：
   - 尽可能遵守项目时间线的规定，如果有困难可以大家讨论调整时间线。如果无法遵守也没有及时告知大家，会拖慢整个项目的进度。
   - 如果数据库有什么问题或者有什么需要，请及时说出来，大家讨论解决。
4. 运行/配置：
   - 数据库连接串 `ConnectionStrings:Oracle`（完整连接串，如 `User Id=liborui;Password=liborui;Data Source=120.27.157.163:1521/XEPDB1`）**不提交仓库**：
     - 本地开发：用 `dotnet user-secrets` 注入（Development 环境自动加载，只存在你本机 `~/.microsoft/usersecrets` 下）：
       ```bash
       cd backend
       dotnet user-secrets set "ConnectionStrings:Oracle" "User Id=你的姓名全拼;Password=你的密码;Data Source=120.27.157.163:1521/XEPDB1"
       ```
     - 生产环境：通过环境变量 `ConnectionStrings__Oracle`（或 KMS/Docker secret）注入，不要写进 `appsettings.json` 或任何提交进仓库的文件。
     - 新增成员参考 `backend/appsettings.example.json` 模板了解需要配置哪些项。
   - 前端环境变量：`frontend/.env.production`、`frontend/.env.development` 已移出版本控制，真实值由 CI/部署注入；仓库内只保留 `frontend/.env.example` 占位模板。
   - JWT 签名密钥 `Jwt:Key` 在 `backend/appsettings.Development.json` 中配置了一个**仅限本地开发（DEV-ONLY）**的随机密钥，保证本地可直接启动。该值仅用于 Development 环境，**生产环境严禁复用**，必须通过环境变量 `Jwt__Key`（或其他密钥管理机制）覆盖，切勿把开发密钥写入 `appsettings.json` 或生产配置。
5. 新人环境准备：
   - 前置条件：Git、**.NET 10 SDK**、**Node.js 22+**；个人数据库账号（账号密码均为姓名全拼，如 liborui/liborui），未开通找数据库管理员。编辑器编码保持 **UTF-8 无 BOM + LF**（Windows cmd 乱码用 `chcp 65001`）。
   - 拉代码：`git clone <仓库地址>` → `git checkout Develop` → `git checkout -b Feature/xxx功能`。
   - 后端：
     ```bash
     cd backend
     dotnet restore
     # 配置数据库连接串（唯一需要自己配的项，UserSecretsId 已内置，无需 init）
     dotnet user-secrets set "ConnectionStrings:Oracle" "User Id=姓名全拼;Password=密码;Data Source=120.27.157.163:1521/XEPDB1"
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
     - 前端 vite proxy 目前指向生产后端（`frontend/vite.config.ts` 中硬编码的 target），开箱即用会直连生产环境、操作生产数据，慎用；想连本地后端需把 target 临时改为 `http://localhost:5146`（本地改、别提交），后续落地 `VITE_DEV_PROXY_TARGET` 后可免改
     - 不要用 `VITE_API_BASE_URL=http://localhost:5146` 直连本地后端：后端未配置 CORS，浏览器会拦截跨域请求
     - `dotnet user-secrets` 与 `frontend/.env.development` 都是本地文件，提交前 `git status` 确认没把它们带进 commit
