using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ShowtimeBackend.Entities.UserPermission;

namespace ShowtimeBackend.TestData
{
    /// <summary>
    /// 用户/权限模块的补充数据：组织架构、批量注册观众账号、登录会话、黑名单、操作日志。
    /// 除批量账号按用户名幂等外，其余表采用“表为空才生成”策略，避免重复执行时数据翻倍。
    /// </summary>
    public partial class TestDataGenerator
    {
        /// <summary>
        /// 生成组织架构（公司 -> 部门）。已存在 ORG_STRUCTURE 数据时跳过。
        /// 返回本次新建的节点数。
        /// </summary>
        private List<OrgStructure> GenerateOrgStructure()
        {
            if (_context.Set<OrgStructure>().Count() > 0)
            {
                return new List<OrgStructure>();
            }

            var nodes = new List<OrgStructure>
            {
                new()
                {
                    OrgCode = "SHOWTIME",
                    OrgName = "Showtime 文化传媒有限公司",
                    OrgType = "COMPANY",
                    SortOrder = 1,
                    Status = true,
                    CreateBy = DefaultActor,
                    UpdateBy = DefaultActor
                },
                new()
                {
                    OrgCode = "DEPT_OPS",
                    OrgName = "运营中心",
                    OrgType = "DEPT",
                    SortOrder = 1,
                    Status = true,
                    CreateBy = DefaultActor,
                    UpdateBy = DefaultActor
                },
                new()
                {
                    OrgCode = "DEPT_TICKET",
                    OrgName = "票务中心",
                    OrgType = "DEPT",
                    SortOrder = 2,
                    Status = true,
                    CreateBy = DefaultActor,
                    UpdateBy = DefaultActor
                },
                new()
                {
                    OrgCode = "DEPT_TECH",
                    OrgName = "技术中心",
                    OrgType = "DEPT",
                    SortOrder = 3,
                    Status = true,
                    CreateBy = DefaultActor,
                    UpdateBy = DefaultActor
                },
                new()
                {
                    OrgCode = "DEPT_MARKET",
                    OrgName = "市场部",
                    OrgType = "DEPT",
                    SortOrder = 4,
                    Status = true,
                    CreateBy = DefaultActor,
                    UpdateBy = DefaultActor
                }
            };

            // 先保存根节点以获得 OrgId，再挂父子关系
            _context.Set<OrgStructure>().Add(nodes[0]);
            _context.SaveChanges();

            for (int i = 1; i < nodes.Count; i++)
            {
                nodes[i].ParentId = nodes[0].OrgId;
                _context.Set<OrgStructure>().Add(nodes[i]);
            }

            _context.SaveChanges();
            return nodes;
        }

        /// <summary>
        /// 批量生成普通观众账号（幂等：用户名已存在则跳过），用于订单/实名/黑名单等展示。
        /// </summary>
        private List<SysUser> GenerateExtraUsers(List<Role> roles, List<OrgStructure> _)
        {
            var created = new List<SysUser>();
            var existingNames = _context.Set<SysUser>().Select(u => u.UserName).ToHashSet();
            var existingPhones = _context.Set<SysUser>().Select(u => u.Phone).ToHashSet();
            var existingEmails = _context.Set<SysUser>()
                .Where(u => u.Email != null)
                .Select(u => u.Email!)
                .ToHashSet();

            var userRole = roles.First(r => r.RoleCode == "USER");
            var passwordHasher = new PasswordHasher<SysUser>();
            var typePool = new[] { "NORMAL", "NORMAL", "NORMAL", "NORMAL", "NORMAL", "MEMBER", "MEMBER", "VIP" };

            for (int i = 0; i < _extraUserCount; i++)
            {
                var userName = $"user{1001 + i}";
                if (existingNames.Contains(userName))
                {
                    continue;
                }

                var phone = GenerateUniquePhone(existingPhones);
                var email = $"{userName}@showtime.test";

                var user = new SysUser
                {
                    UserName = userName,
                    Nickname = GenerateNickname(),
                    Phone = phone,
                    Email = email,
                    UserType = typePool[_random.Next(typePool.Length)],
                    Status = 1,
                    PasswordHash = string.Empty,
                    CreateBy = DefaultActor,
                    UpdateBy = DefaultActor
                };
                user.PasswordHash = passwordHasher.HashPassword(user, TestUserPassword);
                user.UserRoles.Add(new UserRole { Role = userRole });

                // 每个观众至少 1 条已实名记录（供购票流程使用），部分再补 1 条非默认
                user.RealNames.Add(new UserRealName
                {
                    RealName = _faker.Name.FullName(),
                    IdCardNo = GenerateIdCardNo(),
                    IsDefault = true,
                    IsVerified = true,
                    CreateBy = DefaultActor,
                    UpdateBy = DefaultActor
                });
                if (_random.Next(0, 5) == 0)
                {
                    user.RealNames.Add(new UserRealName
                    {
                        RealName = _faker.Name.FullName(),
                        IdCardNo = GenerateIdCardNo(),
                        IsDefault = false,
                        IsVerified = true,
                        CreateBy = DefaultActor,
                        UpdateBy = DefaultActor
                    });
                }

                _context.Set<SysUser>().Add(user);
                created.Add(user);

                existingNames.Add(userName);
                existingPhones.Add(phone);
                existingEmails.Add(email);
            }

            _context.SaveChanges();
            Log($"  Generated {created.Count} extra buyer accounts (idempotent by username)");
            return created;
        }

        private static readonly string[] NicknamePool =
        {
            "风一样的男子", "云淡风轻", "爱喝奶茶的喵", "山间清风", "追风少年",
            "且听风吟", "星河滚烫", "人间理想", "熬夜冠军", "干饭第一名",
            "今天也要加油鸭", "一只咸鱼", "小饼干", "想不出昵称", "用户不存在",
            "晚风吻尽", "偷得浮生半日闲", "恰好心动", "雾里看花", "半盏流年",
            "拾光者", "柠檬不酸", "小熊软糖", "旺旺仙贝", "麦辣鸡腿堡",
            "空调房里吃西瓜", "碎觉觉", "摸鱼大师", "快乐星球球长", "大侠饶命",
            "有点懒", "每天都要早睡", "爱看演出的路人", "剧场的猫", "追光的人",
            "幕间休息", "谢幕之后", "第二排中间", "票根收藏家", "开场前五分钟",
            "喝奶茶不长胖", "明天再减肥", "睡不醒的冬三月", "路过的风", "月亮不睡我不睡",
            "在逃观众", "散场不散伙", "长夜有星光"
        };

        /// <summary>生成常见中文网名风格昵称（部分附加随机数字后缀，减少重复）</summary>
        private string GenerateNickname()
        {
            var nickname = NicknamePool[_random.Next(NicknamePool.Length)];
            if (_random.Next(0, 4) == 0)
            {
                nickname += _random.Next(10, 999);
            }
            return nickname;
        }

        /// <summary>生成不重复的 11 位手机号（1 开头，剩余由随机决定）</summary>
        private string GenerateUniquePhone(HashSet<string> used)
        {
            const string second = "358";
            while (true)
            {
                var phone = "1" + second[_random.Next(second.Length)] +
                    string.Concat(Enumerable.Range(0, 9).Select(_ => (char)('0' + _random.Next(0, 10))));
                if (used.Add(phone))
                {
                    return phone;
                }
            }
        }

        /// <summary>
        /// 生成用户登录会话（USER_SESSION）。表内已有数据时跳过。
        /// </summary>
        private int GenerateUserSessions()
        {
            if (_context.Set<UserSession>().Count() > 0)
            {
                return 0;
            }

            var users = _context.Set<SysUser>()
                .Where(u => u.Status == 1)
                .OrderBy(u => u.UserId)
                .ToList();
            if (users.Count == 0)
            {
                return 0;
            }

            var sessions = new List<UserSession>();
            var now = DateTime.UtcNow;
            foreach (var user in users)
            {
                int count = user.UserName == AdminUserName ? _random.Next(3, 6) : _random.Next(1, 3);
                for (int i = 0; i < count; i++)
                {
                    var loginTime = now.AddHours(-_random.Next(1, 24 * 14));
                    var expireTime = loginTime.AddHours(_random.Next(8, 72));
                    bool isActive = _random.Next(0, 4) != 0 && expireTime > now;
                    sessions.Add(new UserSession
                    {
                        UserId = user.UserId,
                        SessionToken = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N")[..20],
                        LoginTime = loginTime,
                        ExpireTime = isActive ? now.AddHours(_random.Next(6, 72)) : expireTime,
                        LogoutTime = isActive ? null : loginTime.AddHours(_random.Next(2, 8)),
                        IpAddress = GenerateIp(),
                        UserAgent = _faker.PickRandom(new[]
                        {
                            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/126.0",
                            "Mozilla/5.0 (iPhone; CPU iPhone OS 17_5) Safari/604.1",
                            "Mozilla/5.0 (Linux; Android 14) Chrome/126.0 Mobile",
                            "Mozilla/5.0 (Macintosh; Intel Mac OS X 14_5) Safari/605.1"
                        }),
                        RiskFlag = _random.Next(0, 20) == 0,
                        Status = isActive ? "ACTIVE" : _faker.PickRandom(new[] { "EXPIRED", "LOGOUT" }),
                        CreateBy = DefaultActor,
                        UpdateBy = DefaultActor
                    });
                }
            }

            _context.Set<UserSession>().AddRange(sessions);
            _context.SaveChanges();
            return sessions.Count;
        }

        /// <summary>
        /// 生成用户黑名单（USER_BLACKLIST）。表内已有数据时跳过。
        /// </summary>
        private int GenerateUserBlacklist(IReadOnlyCollection<ShowtimeBackend.Entities.ShowSession.Show> shows)
        {
            if (_context.Set<UserBlacklist>().Count() > 0)
            {
                return 0;
            }

            var users = _context.Set<SysUser>()
                .Where(u => u.Status == 1 && u.UserName != AdminUserName)
                .OrderBy(u => u.UserId)
                .ToList();
            if (users.Count == 0)
            {
                return 0;
            }

            var showList = shows.ToList();
            var entries = new List<UserBlacklist>();
            var now = DateTime.UtcNow;
            var reasons = new[]
            {
                "短时间内高频提交退票申请",
                "多次使用非本人实名信息购票",
                "系统识别为黄牛抢票行为",
                "支付回调异常次数过多",
                "同一场次批量下单后集中取消"
            };
            var riskTypes = new[] { "REFUND_ABUSE", "IDENTITY_MISMATCH", "SCALPER", "PAYMENT_RISK", "ORDER_ABUSE" };

            for (int i = 0; i < Math.Min(6, users.Count); i++)
            {
                var user = users[i];
                var idx = _random.Next(riskTypes.Length);
                bool permanent = _random.Next(0, 5) == 0;
                entries.Add(new UserBlacklist
                {
                    UserId = user.UserId,
                    ShowId = showList.Count > 0 && _random.Next(0, 2) == 0
                        ? showList[_random.Next(showList.Count)].ShowId
                        : (long?)null,
                    RiskType = riskTypes[idx],
                    RiskScore = _random.Next(45, 90),
                    StartTime = now.AddDays(-_random.Next(1, 60)),
                    EndTime = permanent ? null : now.AddDays(_random.Next(-20, 30)),
                    IsPermanent = permanent,
                    Reason = reasons[idx],
                    Status = true,
                    CreateBy = DefaultActor,
                    UpdateBy = DefaultActor
                });
            }

            _context.Set<UserBlacklist>().AddRange(entries);
            _context.SaveChanges();
            return entries.Count;
        }

        /// <summary>
        /// 生成操作日志（OPERATION_LOG）。表内已有数据时跳过。
        /// </summary>
        private int GenerateOperationLogs(IReadOnlyCollection<ShowtimeBackend.Entities.ShowSession.Show> shows)
        {
            if (_context.Set<OperationLog>().Count() > 0)
            {
                return 0;
            }

            var users = _context.Set<SysUser>().OrderBy(u => u.UserId).ToList();
            if (users.Count == 0)
            {
                return 0;
            }

            var showList = shows.ToList();
            var logs = new List<OperationLog>();
            var modules = new[]
            {
                "演出管理", "场次管理", "订单管理", "座位管理", "用户管理",
                "退票管理", "改签管理", "营销内容", "登录认证", "电子票"
            };
            var operationTypes = new[]
            {
                "查询", "新增", "修改", "删除", "审核通过", "审核拒绝", "发布", "下架", "登录", "核销"
            };
            var urls = new[]
            {
                "/api/admin/shows", "/api/admin/sessions", "/api/admin/orders",
                "/api/admin/refunds", "/api/admin/exchanges", "/api/auth/login",
                "/api/admin/seat-maps", "/api/admin/tickets/redeem"
            };

            for (int i = 0; i < 220; i++)
            {
                var user = users[_random.Next(users.Count)];
                var module = modules[_random.Next(modules.Length)];
                var showId = module is "演出管理" or "场次管理" or "营销内容" && showList.Count > 0
                    ? showList[_random.Next(showList.Count)].ShowId
                    : (long?)null;
                logs.Add(new OperationLog
                {
                    UserId = user.UserId,
                    UserName = user.UserName,
                    ShowId = showId,
                    OperationModule = module,
                    OperationType = operationTypes[_random.Next(operationTypes.Length)],
                    RequestUrl = urls[_random.Next(urls.Length)],
                    RequestParams = $"{{\"page\":{_random.Next(1, 20)},\"pageSize\":20}}",
                    ResponseResult = "{\"success\":true}",
                    IpAddress = GenerateIp(),
                    UserAgent = "Mozilla/5.0 TestDataGenerator",
                    CostTime = _random.Next(3, 900),
                    Status = _random.Next(0, 12) != 0,
                    ErrorMsg = null,
                    CreateBy = DefaultActor,
                    UpdateBy = DefaultActor
                });
            }

            _context.Set<OperationLog>().AddRange(logs);
            _context.SaveChanges();
            return logs.Count;
        }

        private string GenerateIp()
        {
            var parts = new[]
            {
                _random.Next(1, 223),
                _random.Next(0, 255),
                _random.Next(0, 255),
                _random.Next(1, 254)
            };
            return string.Join(".", parts);
        }
    }
}
