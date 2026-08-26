# SHOWTIME —— 演出活动票务管理平台
本项目为一个演出售票系统，主要用于实现演出活动的票务管理与在线购票功能。系统围绕演出用户与权限管理、信息发布、场次管理、座位安排、订单处理、支付核销等核心业务展开。旨在为用户提供便捷的购票体验，同时协助管理方实现票务数据的统一管理与实时统计分析。
## 技术栈
- 后端框架：ASP.NET Core 10 + C#
- 数据库：Oracle 21c
- ORM：EF Core
- 前端：React + TS/JS
- UI组件库：Ant Design
## 功能点
【用户与权限管理】
1. 多角色权限动态配置
2. 组织架构与数据隔离
3. 操作日志与审计追踪
4. 会话安全与风险控制
5. 用户行为分析与画像

【演出信息发布】
1. 演出信息发布与审核
2. 演出分类与标签管理
3. 演出热度与推荐算法
4. 演出关联营销内容管理

【场次管理】
1.  场次批量生成与冲突检测
2.  场次动态调价策略
3.  场次状态自动化管理
4.  场次限购策略配置
5.  场次销售数据实时看板

【座位安排】
1.  可视化座位图编辑器
2.  座位库存动态锁定与释放
3.  座位分组规则
4.  座位预留与分配管理

【订单处理】
1.  订单智能拆单与合并
2.  退票与改签策略引擎
3.  订单生命周期管理
4.  电子票务生成与防伪

## 相关文档
[PLAN](PLAN.md)
[CONVENTIONS](CONVENTIONS.md)

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
