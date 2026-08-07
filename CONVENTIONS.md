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
   - JWT 签名密钥 `Jwt:Key` 在 `backend/appsettings.Development.json` 中配置了一个**仅限本地开发（DEV-ONLY）**的随机密钥，保证本地可直接启动。该值仅用于 Development 环境，**生产环境严禁复用**，必须通过环境变量 `Jwt__Key`（或其他密钥管理机制）覆盖，切勿把开发密钥写入 `appsettings.json` 或生产配置。
