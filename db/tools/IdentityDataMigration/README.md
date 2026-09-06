# USER_REAL_NAME 历史明文迁移

该工具把 `APP_OWNER.USER_REAL_NAME.ID_CARD_NO` 中尚未使用 `v1.` 格式保护的历史值转换为 AES-GCM 密文。它只执行 DML，不修改数据库 Schema。

## 运行前

1. 备份目标数据并确认使用的是正确环境。
2. 使用与后端完全相同的 `IdentityData__EncryptionKey`；密钥必须是 Base64 编码的 32 字节值。
3. 设置完整 Oracle 连接串。不要把连接串或密钥写进仓库、命令历史或截图。

PowerShell 当前窗口示例：

```powershell
$env:ConnectionStrings__Oracle = 'User Id=<账号>;Password=<密码>;Data Source=//120.27.157.163:1521/XEPDB1'
$env:IdentityData__EncryptionKey = '<与后端相同的 Base64 密钥>'
```

## 1. Dry-run

默认只统计，不更新数据库：

```powershell
dotnet run --project db/tools/IdentityDataMigration
```

确认目标库和 legacy 行数正确后再继续。

## 2. 执行迁移

```powershell
dotnet run --project db/tools/IdentityDataMigration -- --apply
```

工具按 100 行分批处理，只输出 `REAL_NAME_ID` 范围，不输出姓名或身份证号；已经以 `v1.` 开头的行会被跳过，因此可以安全重跑。

## 3. 校验

使用有权读取 `APP_OWNER` 的个人账号执行：

```sql
SELECT COUNT(*) AS LEGACY_COUNT
FROM APP_OWNER.USER_REAL_NAME
WHERE ID_CARD_NO NOT LIKE 'v1.%';
```

期望结果为 `0`。然后通过实名列表 API 抽查脱敏响应和下单实名校验。

## 故障处理

- 未带 `--apply` 时数据库不会被修改。
- 若密钥错误，既有 `v1.` 数据将无法解密；不要尝试用新密钥覆盖。
- 若迁移中断，已完成批次保持有效；排除连接问题后使用同一密钥重跑。
- 回滚应恢复运行前备份，不能通过日志找回身份证明文。
