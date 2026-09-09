# JMeter 压测脚本

## 文件说明

| 文件 | 说明 |
|------|------|
| `showtime_load_test.jmx` | JMeter 主压测脚本（登录→锁座→下单完整链路，仅建议小线程冒烟） |
| `showtime_business_only.jmx` | JMeter 业务链路压测脚本（预热 token 模式：锁座→下单，用于高并发档） |
| `test_users.csv` | 测试用户数据（账号/密码，共 100 个） |
| `seed_users.py` | 批量创建测试账号（Python，尊重 register 限流） |
| `warmup_tokens.py` | token 预热工具：登录取 token / 滚动刷新 / 状态校验 |
| `generate_load_plan.py` | 生成分配计划 CSV（sessionId,seatId,priceStrategyId，按座位所在区匹配定价策略） |
| `out/` | 压测产物目录（jtl/HTML/token/座位计划，已 gitignore，不入库） |

## 压测链路

每个虚拟用户一轮迭代执行：**登录 → 锁座 → 下单**。

- 每个线程固定使用 `seatId = SEAT_MIN + 线程号 - 1`，并发下不同线程不抢同一座位；
- 下单使用锁座接口返回的真实 `lockToken`，不伪造锁令牌。

## 前置条件

1. 后端已启动（默认 `http://localhost:5146`，可用 `-JDOMAIN/-JPORT/-JPROTOCOL` 覆盖）。
2. 场次与座位：`SESSION_ID`（默认 1）场次存在，且包含 `SEAT_MIN .. SEAT_MIN+THREADS-1`
   这些**未售座位**；`PRICE_STRATEGY_ID`（默认 1）是该场次有效的定价策略。
3. 测试账号：CSV 中账号在真实库不存在会导致登录 401，先建号：

```bash
cd test/jmeter
# 建 100 个（与 CSV 一致）。register 限流 3 次/分钟/IP，约需 35 分钟；本地冒烟可用 --count 10
python3 seed_users.py --count 100
```

## 单机运行

```bash
cd test/jmeter
jmeter -n -t showtime_load_test.jmx -JTHREADS=50 -JLOOPS=1 -l results.jtl -e -o report/
```

常用参数：`-JTHREADS`、`-JLOOPS`、`-JRAMP_UP`、`-JSESSION_ID`、`-JSEAT_MIN`、`-JPRICE_STRATEGY_ID`。

> 注意：login 接口限流 **5 次/分钟/IP**，单机大并发必然 429；单机建议小线程冒烟，
> 高并发档请用下面的「预热 token 模式」。

## 预热 token 模式（高并发档，推荐）

受 login 5/min/IP 与 accessToken 15 分钟有效期的约束，高并发档（≥100 线程）无法现场登录。
三段式流程：**建号 → 预热 → 业务压测**。

```bash
cd test/jmeter
BASE=http://120.27.157.163:5146

# ① 建号+预热：登录全部账号（4/min 节流，缺失账号自动补建），
#    产出 out/tokens.csv（account,accessToken,refreshToken,expiresAtUtc），约 27 分钟
python3 warmup_tokens.py --base-url $BASE

# ② 生成本档座位分配计划（行数 = 本档线程数；实时避开已售/已锁座位）
python3 generate_load_plan.py --base-url $BASE --count 500 --out out/plan_500.csv

# ③ 业务压测（锁座→下单；THREADS 必须 = plan 行数）
jmeter -n -t showtime_business_only.jmx \
  -JDOMAIN=120.27.157.163 -JTHREADS=500 -JRAMP_UP=60 -JLOOPS=1 \
  -JTOKEN_FILE=out/tokens.csv -JPLAN_FILE=out/plan_500.csv \
  -l out/2026-XX-XX/results-500.jtl -e -o out/2026-XX-XX/report-500/
```

- **token 有效期管理**：accessToken 15 分钟过期、refresh 限流 10/min/IP。多档连续压测时，
  每档前跑 `python3 warmup_tokens.py --refresh --base-url $BASE`（滚动刷新全部 token，≈8/min，
  100 个约 13 分钟），刷新完立即开跑；`--status` 可随时查看剩余有效期。
- **同 token 多线程复用**：TOKEN_FILE 轮转复用（100 token 支撑任意线程数），受认证用户全局
  120/min/人限流约束：每线程每轮 2 请求（锁座+下单），线程数 × 2 ÷ token 数需留足余量。
- **座位与定价策略**：下单要求 priceStrategyId 与座位所在票区一致，generate_load_plan.py 已按区匹配，
  不要手改 plan CSV 的 priceStrategyId 列。另：场次 138 无任何定价策略，不可下单，分配脚本已自动跳过。
- **登录风控提示**：同一账号换 IP/UA 重新登录会触发异地风控，旧会话被 LOCK（refresh 返回 `AUTH_SESSION_LOCKED`）。
  预热后不要再用不同客户端（如 JMeter 直登模式）登录同一批账号，否则需重新预热受影响账号。
- **档间数据**：订单为 PENDING_PAY 状态，约 15 分钟未支付自动过期并释放座位（实测确认）。
  档间座位池会自回补；若需连续快速重压同批座位，需等上一档订单过期或管理端清理。

## 分布式运行（多机分摊限流与流量，登录模式）

1. 每台 slave 启动 `jmeter-server`，并给不同 slave 配不同的 `SEAT_MIN` 起点，避免抢同一批座位：

```bash
# slave1: 使用 1..THREADS
jmeter-server -JSEAT_MIN=1
# slave2: 使用 201..200+THREADS（按实际座位数调整偏移）
jmeter-server -JSEAT_MIN=201
```

2. master 汇总执行（`THREADS` 为每台 slave 的线程数，座位范围需覆盖 `THREADS × slave 数`）：

```bash
jmeter -n -t showtime_load_test.jmx -R slave1,slave2 -JTHREADS=200 -l results.jtl -e -o report/
```

> `-J` 参数只作用于本机；slave 各自的 `SEAT_MIN` 需在其启动参数中配置。

## 常见问题

- 登录 401：CSV 账号未创建，先运行 `seed_users.py`。
- 登录 429：login 限流 5 次/分钟/IP，降低 `THREADS` 或改为多机压测。
- 锁座 4xx：`SESSION_ID`/座位号不存在、座位已售或已被他人锁定。
- 下单 4xx：`lockToken` 过期或座位已被买走；确认 `SEAT_MIN` 覆盖范围足够大。
- 失败遗留的 ACTIVE 锁默认 600 秒自动过期；重跑前可等待或换一批 `SEAT_MIN`。
