# SHOWTIME 前端（React + TypeScript + Vite）

SHOWTIME 票务平台前端，包含用户端（浏览演出、选座购票、订单、个人中心）与管理端（数据看板、用户/演出/场次/订单/退改签/策略/核销/座位图/营销管理）。

## 技术栈

- React 19 + TypeScript + Vite 8
- Ant Design 6（@ant-design/icons）
- React Router 7
- openapi-fetch（类型来自 `src/api/openapi.json` 快照）
- @microsoft/signalr（订单/退款实时通知）
- ECharts（管理端看板）、react-window（虚拟列表）、qrcode.react（电子票二维码）
- Oxlint（代码检查）、Playwright（E2E，mock API）

## 环境要求

- Node.js `^20.19.0` 或 `>=22.12.0`（Vite 8 要求，CI 使用 22）
- 包管理器使用 npm（锁文件 `package-lock.json`）

## 常用命令

| 命令 | 说明 |
|------|------|
| `npm install` | 安装依赖（CI 用 `npm ci`） |
| `npm run dev` | 启动开发服务器（默认 http://localhost:5173，HMR） |
| `npm run build` | 类型检查（tsc -b）+ 生产构建，产物在 `dist/` |
| `npm run preview` | 本地预览生产构建 |
| `npm run lint` | Oxlint 代码检查 |
| `npx playwright test` | Playwright E2E（通过 `page.route` 全量 mock `/api/**`，不连真实后端） |
| `node --experimental-strip-types scripts/seatMapLayout.check.ts` | 座位图排版冒烟校验脚本 |

## 环境变量

模板见 `.env.example`，复制为 `.env.development` / `.env.production` 使用（本地文件，勿提交）：

| 变量 | 说明 |
|------|------|
| `VITE_API_BASE_URL` | API 基础地址。留空 = 同域（走 vite 代理 / nginx 反代）；非空 = 跨域直连（后端未配置 CORS，不推荐） |
| `VITE_DEV_PROXY_TARGET` | 开发代理目标。默认指向生产后端 `http://120.27.157.163:5146`（见 `vite.config.ts`）；本地全栈联调设为 `http://localhost:5146` |

## 开发代理

`vite.config.ts` 已配置 `/api`、`/files`（本地磁盘存储静态托管）、`/hubs`（SignalR WebSocket）代理到 `VITE_DEV_PROXY_TARGET`。本地联调时在 `.env.development` 设置 `VITE_DEV_PROXY_TARGET=http://localhost:5146`（**勿提交**）。

## 目录说明

```
src/
├── api/             # openapi-fetch 客户端、request.ts（Token 注入/401 处理）、类型与 openapi.json 快照
├── components/      # Layout、FileUploader 等公共组件
├── context/         # UserContext（登录用户信息）
├── pages/           # 用户端（Home/Search/PerformanceDetail/SeatSelection/Order/OrderDetail/UserCenter/Login/Register）与管理端（admin/）
├── realtime/        # SignalR 实时通知连接
├── router/          # 路由配置
├── types/           # 后端 DTO 类型
└── main.tsx / index.css
tests/               # Playwright E2E（tests/mocks/api.ts 为接口 mock）
scripts/             # 工具脚本（座位图排版校验）
```

## 接口契约

- 后端 Scalar/OpenAPI 在开发环境生成：http://localhost:5146/scalar/v1
- CI 校验 `src/api/openapi.json` 快照与后端输出一致；后端接口变更后需重新导出并提交该快照。
- 接口约定、错误码与状态枚举见根目录 [doc/API.md](../doc/API.md)。
