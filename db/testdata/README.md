# 测试数据生成工具

> 生成器会写入实名测试数据。运行前必须设置与后端一致的
> `IdentityData__EncryptionKey`（Base64 编码的 32 字节密钥），否则会拒绝执行；
> 密钥不得写入 `appsettings.json` 或提交到 Git。

## 1. 功能范围

生成器面向**当前完整 schema（38 张表）**，覆盖用户/权限、演出/场次/座位/票价主体数据，
以及后端各阶段新增模块的支撑数据与交易数据：

| 分组 | 表 | 说明 |
|---|---|---|
| 用户与权限 | `ORG_STRUCTURE`、`ROLE`、`SYS_USER`、`USER_ROLE`、`PERMISSION`、`ROLE_PERMISSION`、`USER_REAL_NAME`、`USER_BLACKLIST`、`USER_SESSION`、`OPERATION_LOG` | 组织架构、管理员/运营/观众账号（批量）、实名、登录会话、黑名单、操作日志 |
| 演出主体 | `CATEGORY`、`TAG`、`SHOW`、`SHOW_TAG`、`VENUE`、`SEAT_MAP`、`SEAT_SECTION`、`SEAT`、`SHOW_SESSION`、`PRICE_STRATEGY`、`PURCHASE_LIMIT` | 演出、分类标签、场馆座位图、场次票价、限购 |
| 营销/策略支撑 | `MARKETING_CONTENT`、`DYNAMIC_PRICING_RULE`、`SEAT_RULE`、`SEAT_RULE_SCOPE`、`REFUND_POLICY`、`EXCHANGE_POLICY` | 公告/广告/促销、动态调价、选座规则、退票改签策略 |
| 订单/票务交易 | `T_ORDER`、`ORDER_ITEM`、`PAYMENT`、`E_TICKET`、`SEAT_LOCK`、`SEAT_RESERVATION`、`REFUND_REQUEST`、`REFUND_ITEM`、`EXCHANGE_REQUEST`、`EXCHANGE_ITEM` | 已结束/在售场次的订单历史、支付、出票、锁座/占座、退票、改签，状态互相一致 |

**幂等性约定**（可重复执行）：
- 角色、权限、测试账号、批量观众账号按业务键去重；
- 演出主体数据：检测到 `CATEGORY/SEAT_MAP/VENUE` 已有数据则跳过；
- 支撑数据、用户会话/黑名单/操作日志：目标表已有数据则跳过；
- 交易数据：`T_ORDER` 已有订单则跳过。

> 注意：主体演出数据（CATEGORY/SEAT_MAP/VENUE 等）若已存在则跳过生成，避免重复；
> 如需重置请先清空相关业务表（见 `reset_business_data.sql`）后重跑。

## 2. 运行方式

```bash
cd db/testdata
# 设置实名加密密钥（32 字节 Base64），与后端保持一致
export IdentityData__EncryptionKey="<Base64-32-bytes>"
dotnet run -- "User Id=<姓名全拼>;Password=<密码>;Data Source=<host>:1521/XEPDB1"
```

也可以不传连接串，改用 `appsettings.json` 中 `ConnectionStrings:DefaultConnection`。

生成期间会打印每阶段数量与最终统计；非交互环境（CI/管道重定向）不会阻塞等待按键。

## 3. 配置说明（appsettings.json DataGeneration）

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "User Id=your_user;Password=your_password;Data Source=//host:1521/XEPDB1"
  },
  "DataGeneration": {
    "ShowCount": 10,
    "MinSessionsPerShow": 3,
    "MaxSessionsPerShow": 5,
    "SeatsPerSession": 200,
    "EnableDetailedLog": true,
    "ExtraUserCount": 60,
    "EnableOrderSeed": true,
    "EndedSessionSoldRatio": 0.65,
    "OnSaleSessionSoldRatio": 0.18,
    "RefundOrderRatio": 0.06,
    "ExchangeOrderRatio": 0.02
  },
  "IdentityData": {
    "EncryptionKey": "Set the IdentityData__EncryptionKey environment variable; never store the real key here."
  }
}
```

| 键 | 默认值 | 含义 |
|---|---|---|
| `ExtraUserCount` | 60 | 除 admin/testuser1~3 外额外生成的观众账号数（用户名 `user1001` 起，幂等） |
| `EnableOrderSeed` | true | 是否生成订单/支付/票务等交易数据 |
| `EndedSessionSoldRatio` | 0.65 | 已结束场次的售出比例 |
| `OnSaleSessionSoldRatio` | 0.18 | 在售场次的售出比例（保留足够余座给选座演示） |
| `RefundOrderRatio` | 0.06 | 已出票订单中产生退票申请的比例 |
| `ExchangeOrderRatio` | 0.02 | 已出票订单中产生改签的比例 |

> 降低 `EnableOrderSeed`/比例可显著缩短运行时间；提高 `ExtraUserCount` 可让
> 用户分析看板/实名购票演示有更多数据。

## 4. 测试账号

**登录/注册接口可直接使用**：
- 管理员：`admin` / `Admin@12345`（Admin + USER 角色）
- 普通用户：`testuser1`~`testuser3` / `Test@12345`
- 批量观众：`user1001`~`user1060`（密码与 testuser 相同 `Test@12345`）

## 5. 验证

`VerifyTestData.sql` 提供常用数据量/分布校验 SQL（在 APP_OWNER schema 下执行）。
