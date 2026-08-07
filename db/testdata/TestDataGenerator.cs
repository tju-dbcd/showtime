using System;
using System.Collections.Generic;
using System.Linq;
using Bogus;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ShowtimeBackend.Data;
using ShowtimeBackend.Entities.SeatZone;
using ShowtimeBackend.Entities.ShowSession;
using ShowtimeBackend.Entities.UserPermission;

namespace ShowtimeBackend.TestData
{
    public class TestDataGenerator
    {
        private readonly AppDbContext _context;
        private readonly Random _random = new Random();
        private readonly Faker _faker = new Faker("zh_CN");

        private readonly int _showCount;
        private readonly int _minSessions;
        private readonly int _maxSessions;
        private readonly int _seatsPerSession;
        private readonly bool _enableDetailedLog;

        /// <summary>测试管理员账号（拥有 Admin + USER 角色）</summary>
        public const string AdminUserName = "admin";
        public const string AdminPassword = "Admin@12345";
        /// <summary>普通测试用户账号密码</summary>
        public const string TestUserPassword = "Test@12345";
        private const string DefaultActor = "TestDataGenerator";

        public TestDataGenerator(
            AppDbContext context,
            int showCount = 10,
            int minSessions = 3,
            int maxSessions = 5,
            int seatsPerSession = 200,
            bool enableDetailedLog = true)
        {
            _context = context;
            _showCount = showCount;
            _minSessions = minSessions;
            _maxSessions = maxSessions;
            _seatsPerSession = seatsPerSession;
            _enableDetailedLog = enableDetailedLog;
        }

        public void GenerateAllData()
        {
            Log("=== Starting Test Data Generation ===");

            using var transaction = _context.Database.BeginTransaction();

            try
            {
                // ---- 用户与权限模块（幂等：已存在的角色/用户/权限不重复创建）----
                var roles = GenerateRoles();
                var users = GenerateUsers(roles);
                GeneratePermissions(roles);
                Log($"[1/13] User & Permission module: {roles.Count} roles, {users.Count} users");

                // ---- 演出/座位主体数据（已存在则跳过，防止重复爆炸）----
                if (HasExistingShowData())
                {
                    Log("检测到已存在演出/座位类数据，跳过主体数据生成。");
                    Log("如需重新生成，请先清空相关业务表（或手动处理冲突）后重跑。");
                    transaction.Commit();
                    Log("=== Test Data Generation Completed (main data skipped) ===");
                    PrintStatistics();
                    return;
                }

                var categories = GenerateCategories();
                _context.Set<Category>().AddRange(categories);
                _context.SaveChanges();
                Log($"[2/13] Generated {categories.Count} categories");

                var tags = GenerateTags();
                _context.Set<Tag>().AddRange(tags);
                _context.SaveChanges();
                Log($"[3/13] Generated {tags.Count} tags");

                var venues = GenerateVenues();
                _context.Set<Venue>().AddRange(venues);
                _context.SaveChanges();
                Log($"[4/13] Generated {venues.Count} venues");

                var seatMaps = GenerateSeatMaps(venues);
                _context.Set<SeatMap>().AddRange(seatMaps);
                _context.SaveChanges();
                Log($"[5/13] Generated {seatMaps.Count} seat maps");

                var seatSections = GenerateSeatSections(seatMaps);
                _context.Set<SeatSection>().AddRange(seatSections);
                _context.SaveChanges();
                Log($"[6/13] Generated {seatSections.Count} seat sections");

                var seats = GenerateSeats(seatSections);
                _context.Set<Seat>().AddRange(seats);
                _context.SaveChanges();
                Log($"[7/13] Generated {seats.Count} seats");

                var shows = GenerateShows(categories);
                _context.Set<Show>().AddRange(shows);
                _context.SaveChanges();
                Log($"[8/13] Generated {shows.Count} shows");

                var showTags = GenerateShowTags(shows, tags);
                _context.Set<ShowTag>().AddRange(showTags);
                _context.SaveChanges();
                Log($"[9/13] Generated {showTags.Count} show-tag associations");

                var showSessions = GenerateShowSessions(shows, seatMaps);
                _context.Set<ShowSession>().AddRange(showSessions);
                _context.SaveChanges();
                Log($"[10/13] Generated {showSessions.Count} show sessions");

                var priceStrategies = GeneratePriceStrategies(showSessions, seatSections);
                _context.Set<PriceStrategy>().AddRange(priceStrategies);
                _context.SaveChanges();
                Log($"[11/13] Generated {priceStrategies.Count} price strategies");

                var purchaseLimits = GeneratePurchaseLimits(shows, showSessions);
                _context.Set<PurchaseLimit>().AddRange(purchaseLimits);
                _context.SaveChanges();
                Log($"[12/13] Generated {purchaseLimits.Count} purchase limits");

                transaction.Commit();

                Log("=== Test Data Generation Completed ===");
                PrintStatistics();
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                Log($"ERROR: {ex.Message}");
                var inner = ex.InnerException;
                while (inner is not null)
                {
                    Log($"  INNER: {inner.GetType().Name}: {inner.Message}");
                    inner = inner.InnerException;
                }
                throw;
            }
        }

        #region Data Generation Methods

        private List<Category> GenerateCategories()
        {
            var categoryNames = new[]
            {
                "话剧", "音乐剧", "演唱会", "舞蹈",
                "戏曲", "儿童剧", "音乐会", "脱口秀"
            };

            var categories = new List<Category>();
            int order = 0;

            foreach (var name in categoryNames)
            {
                categories.Add(new Category
                {
                    CategoryName = name,
                    ParentId = null,
                    SortOrder = ++order,
                    Status = 1
                });
            }

            return categories;
        }

        private List<Tag> GenerateTags()
        {
            var tagData = new[]
            {
                ("热门", "#FF6B6B"),
                ("经典", "#4ECDC4"),
                ("新剧", "#45B7D1"),
                ("亲子", "#96CEB4"),
                ("情侣", "#FFEAA7"),
                ("高分", "#DDA0DD"),
                ("获奖", "#F39C12"),
                ("独家", "#E74C3C"),
                ("首演", "#1ABC9C"),
                ("限时", "#E67E22")
            };

            var tags = new List<Tag>();
            foreach (var (name, color) in tagData)
            {
                tags.Add(new Tag
                {
                    TagName = name,
                    Color = color,
                    Status = _random.Next(0, 3) == 0 ? 0 : 1
                });
            }
            return tags;
        }

        private List<Venue> GenerateVenues()
        {
            var venueData = new[]
            {
                ("国家大剧院", "北京市西城区西长安街2号", "010-66550000"),
                ("天桥艺术中心", "北京市西城区天桥南大街9号", "010-83156170"),
                ("保利剧院", "北京市东城区东直门南大街14号", "010-65065343"),
                ("上海文化广场", "上海市黄浦区复兴中路597号", "021-54619988"),
                ("广州大剧院", "广州市天河区珠江新城珠江西路1号", "020-38392888"),
                ("深圳保利剧院", "深圳市南山区后海滨路3013号", "0755-86371698"),
                ("杭州大剧院", "杭州市江干区钱江新城新业路39号", "0571-86855018"),
                ("成都城市音乐厅", "成都市武侯区一环路南一段20号", "028-68555555")
            };

            var venues = new List<Venue>();
            int venueCount = _random.Next(4, Math.Min(7, venueData.Length + 1));
            var selectedVenues = venueData.OrderBy(x => Guid.NewGuid()).Take(venueCount);

            foreach (var (name, address, phone) in selectedVenues)
            {
                venues.Add(new Venue
                {
                    VenueName = name,
                    Address = address,
                    ContactPhone = phone,
                    Status = _random.Next(0, 2) == 0 ? "ENABLED" : "DISABLED",
                    Remark = _random.Next(0, 3) == 0 ? "测试场馆-" + _faker.Lorem.Word() : null
                });
            }
            return venues;
        }

        private List<SeatMap> GenerateSeatMaps(List<Venue> venues)
        {
            var seatMaps = new List<SeatMap>();
            int mapCount = Math.Min(_random.Next(3, 6), venues.Count);

            foreach (var venue in venues.Take(mapCount))
            {
                int mapsPerVenue = _random.Next(1, 3);
                for (int i = 0; i < mapsPerVenue; i++)
                {
                    seatMaps.Add(new SeatMap
                    {
                        VenueId = venue.VenueId,
                        MapCode = $"MAP_{venue.VenueId}_{(char)('A' + i)}",
                        MapName = $"座位图{(char)('A' + i)}",
                        MapVersion = $"V{_random.Next(1, 4)}",
                        IsDefault = i == 0,
                        MapWidth = 800 + _random.Next(0, 400),
                        MapHeight = 600 + _random.Next(0, 300),
                        MapStatus = _random.Next(0, 3) == 0 ? "DRAFT" : "ENABLED",
                        Remark = _random.Next(0, 3) == 0 ? "测试座位图" : null
                    });
                }
            }

            if (seatMaps.Count == 0 && venues.Any())
            {
                seatMaps.Add(new SeatMap
                {
                    VenueId = venues.First().VenueId,
                    MapCode = "MAP_DEFAULT",
                    MapName = "默认座位图",
                    MapVersion = "V1",
                    IsDefault = true,
                    MapWidth = 1000,
                    MapHeight = 800,
                    MapStatus = "ENABLED",
                    Remark = "默认座位图"
                });
            }

            return seatMaps;
        }

        private List<SeatSection> GenerateSeatSections(List<SeatMap> seatMaps)
        {
            var sections = new List<SeatSection>();
            var sectionConfigs = new[]
            {
                ("VIP区", "VIP", "#E74C3C"),
                ("A区", "NORMAL", "#3498DB"),
                ("B区", "NORMAL", "#2ECC71"),
                ("C区", "NORMAL", "#F39C12"),
                ("D区", "NORMAL", "#9B59B6"),
                ("包厢", "NORMAL", "#1ABC9C")
            };

            foreach (var seatMap in seatMaps)
            {
                int sectionCount = _random.Next(3, Math.Min(6, 7));
                int sectionsPerMap = Math.Min(sectionCount, sectionConfigs.Length);

                for (int i = 0; i < sectionsPerMap; i++)
                {
                    var (name, type, color) = sectionConfigs[i];

                    sections.Add(new SeatSection
                    {
                        SeatMapId = seatMap.SeatMapId,
                        SectionCode = $"SEC_{(char)('A' + i)}",
                        SectionName = name,
                        SectionType = type,
                        SectionColor = color,
                        FloorNo = _random.Next(0, 2) == 0 ? null : $"{_random.Next(1, 4)}F",
                        IsSellable = true,
                        DisplayOrder = i,
                        Remark = _random.Next(0, 3) == 0 ? $"测试区域 {name}" : null
                    });
                }
            }
            return sections;
        }

        private List<Seat> GenerateSeats(List<SeatSection> seatSections)
        {
            var seats = new List<Seat>();
            int totalSeatsGenerated = 0;
            int targetSeatsPerSection = _seatsPerSession / Math.Max(1, seatSections.Count);

            foreach (var section in seatSections)
            {
                int rows = _random.Next(8, 15);
                int cols = _random.Next(8, 12);

                if (_random.Next(0, 2) == 0)
                {
                    rows = 10;
                    cols = 10;
                }

                int seatsToGenerate = Math.Min(targetSeatsPerSection, rows * cols);

                if (_random.Next(0, 2) == 0 && totalSeatsGenerated + seatsToGenerate > _seatsPerSession * 2)
                {
                    seatsToGenerate = Math.Max(50, seatsToGenerate / 2);
                }

                int generated = 0;
                for (int row = 0; row < rows && generated < seatsToGenerate; row++)
                {
                    for (int col = 0; col < cols && generated < seatsToGenerate; col++)
                    {
                        string rowCode = $"{(char)('A' + row)}";
                        string seatNo = $"{rowCode}{col + 1}";

                        decimal xCoord = 50 + (col * 30) + _random.Next(-5, 6);
                        decimal yCoord = 50 + (row * 30) + _random.Next(-5, 6);

                        seats.Add(new Seat
                        {
                            SeatSectionId = section.SeatSectionId,
                            RowCode = rowCode,
                            SeatNo = seatNo,
                            RowIndex = row,
                            ColIndex = col,
                            XCoord = xCoord,
                            YCoord = yCoord,
                            SeatType = _random.Next(0, 5) == 0 ? "COUPLE" : "NORMAL",
                            SeatStatus = _random.Next(0, 5) == 0 ? "DISABLED" : "ENABLED",
                            IsAisleSide = (col == 0 || col == cols - 1) && _random.Next(0, 2) == 0,
                            IsSellable = true,
                            Remark = null
                        });
                        generated++;
                        totalSeatsGenerated++;
                    }
                }
            }

            Log($"  Generated {totalSeatsGenerated} seats across all sections");
            return seats;
        }

        private List<Show> GenerateShows(List<Category> categories)
        {
            var showNames = new[]
            {
                "茶馆", "雷雨", "日出", "北京人", "原野",
                "歌剧魅影", "猫", "悲惨世界", "摇滚莫扎特", "汉密尔顿",
                "天鹅湖", "胡桃夹子", "睡美人", "吉赛尔", "卡门",
                "牡丹亭", "长生殿", "桃花扇", "西厢记", "红楼梦",
                "冰雪奇缘", "狮子王", "美女与野兽", "阿拉丁", "小美人鱼"
            };

            var statuses = new[] { "DRAFT", "PUBLISHED", "PUBLISHED", "PUBLISHED", "UNPUBLISHED" };
            var auditStatuses = new[] { "PENDING", "APPROVED", "APPROVED", "APPROVED", "REJECTED" };

            var shows = new List<Show>();
            var shuffledNames = showNames.OrderBy(x => Guid.NewGuid()).Take(_showCount).ToList();

            for (int i = 0; i < _showCount; i++)
            {
                var category = categories[_random.Next(categories.Count)];

                shows.Add(new Show
                {
                    ShowName = shuffledNames[i % shuffledNames.Count],
                    CategoryId = category.CategoryId,
                    Description = $"{shuffledNames[i % shuffledNames.Count]} - 精彩演出，不容错过！" +
                                 $"{_faker.Lorem.Sentence(10)}",
                    DurationMinutes = new[] { 90, 120, 150, 180, 210 }[_random.Next(5)],
                    PosterUrl = $"https://posters.example.com/show_{i + 1}_{Guid.NewGuid():N}.jpg",
                    Status = statuses[_random.Next(statuses.Length)],
                    AuditStatus = auditStatuses[_random.Next(auditStatuses.Length)],
                    AuditBy = _random.Next(0, 3) == 0 ? null : "admin_" + _random.Next(1, 5),
                    AuditTime = _random.Next(0, 3) == 0 ? null : DateTime.Now.AddDays(-_random.Next(1, 60))
                });
            }
            return shows;
        }

        private List<ShowTag> GenerateShowTags(List<Show> shows, List<Tag> tags)
        {
            var showTags = new List<ShowTag>();
            var usedCombinations = new HashSet<string>();

            foreach (var show in shows)
            {
                int tagCount = _random.Next(2, Math.Min(5, tags.Count + 1));
                var selectedTags = tags.OrderBy(x => Guid.NewGuid()).Take(tagCount);

                foreach (var tag in selectedTags)
                {
                    var key = $"{show.ShowId}_{tag.TagId}";
                    if (!usedCombinations.Contains(key))
                    {
                        usedCombinations.Add(key);
                        showTags.Add(new ShowTag
                        {
                            ShowId = show.ShowId,
                            TagId = tag.TagId
                        });
                    }
                }
            }
            return showTags;
        }

        private List<ShowSession> GenerateShowSessions(List<Show> shows, List<SeatMap> seatMaps)
        {
            var showSessions = new List<ShowSession>();
            var statuses = new[] { "UPCOMING", "PRESALE", "ONSALE", "ONSALE", "SOLD_OUT", "ENDED" };

            foreach (var show in shows)
            {
                int sessionCount = _random.Next(_minSessions, _maxSessions + 1);
                var availableSeatMaps = seatMaps.OrderBy(x => Guid.NewGuid())
                    .Take(Math.Min(2, seatMaps.Count)).ToList();

                if (!availableSeatMaps.Any()) continue;

                for (int i = 0; i < sessionCount; i++)
                {
                    DateTime baseDate;

                    if (_random.Next(0, 2) == 0)
                    {
                        baseDate = DateTime.Now.AddDays(_random.Next(-30, 10));
                    }
                    else
                    {
                        baseDate = DateTime.Now.AddDays(_random.Next(10, 90));
                    }

                    var startTime = baseDate.Date.AddHours(_random.Next(14, 21));
                    var duration = show.DurationMinutes ?? 120;
                    var endTime = startTime.AddMinutes(duration + _random.Next(0, 15));

                    var saleStart = startTime.AddDays(-_random.Next(7, 30));
                    var saleEnd = startTime.AddDays(-_random.Next(1, 7));

                    var seatMap = availableSeatMaps[_random.Next(availableSeatMaps.Count)];

                    showSessions.Add(new ShowSession
                    {
                        ShowId = show.ShowId,
                        SeatMapId = seatMap.SeatMapId,
                        StartTime = startTime,
                        EndTime = endTime,
                        SaleStartTime = saleStart,
                        SaleEndTime = saleEnd,
                        SessionStatus = statuses[_random.Next(statuses.Length)]
                    });
                }
            }
            return showSessions;
        }

        private List<PriceStrategy> GeneratePriceStrategies(List<ShowSession> showSessions, List<SeatSection> seatSections)
        {
            var strategies = new List<PriceStrategy>();
            var priceTypes = new[] { "EARLY_BIRD", "PRESALE", "STANDARD", "VIP", "MEMBER" };
            var typeNames = new Dictionary<string, string>
            {
                { "EARLY_BIRD", "早鸟票" },
                { "PRESALE", "预售票" },
                { "STANDARD", "普通票" },
                { "VIP", "VIP票" },
                { "MEMBER", "会员票" }
            };

            foreach (var session in showSessions)
            {
                var sections = seatSections.Where(s => s.SeatMapId == session.SeatMapId).ToList();
                if (!sections.Any()) continue;

                foreach (var section in sections)
                {
                    int strategyCount = _random.Next(2, 4);
                    var shuffledTypes = priceTypes.OrderBy(x => Guid.NewGuid()).Take(strategyCount);

                    int priority = 10;
                    foreach (var type in shuffledTypes)
                    {
                        decimal basePrice = 80 + (section.SectionType == "VIP" ? 150 : 50) + (_random.Next(0, 20) * 5);
                        var saleStart = session.SaleStartTime.AddDays(_random.Next(0, 5));
                        var saleEnd = session.SaleEndTime.AddDays(-_random.Next(0, 3));

                        if (saleStart >= saleEnd)
                        {
                            saleStart = session.SaleStartTime;
                            saleEnd = session.SaleEndTime;
                        }

                        strategies.Add(new PriceStrategy
                        {
                            SessionId = session.SessionId,
                            SeatSectionId = section.SeatSectionId,
                            StrategyName = $"{section.SectionName}-{typeNames[type]}",
                            PriceType = type,
                            Price = Math.Round(basePrice, 2),
                            SaleStartTime = saleStart,
                            SaleEndTime = saleEnd,
                            Priority = priority,
                            Quota = type == "EARLY_BIRD" ? _random.Next(10, 30) : (int?)null,
                            Status = "ENABLED"
                        });

                        priority += 10;
                    }
                }
            }
            return strategies;
        }

        private List<PurchaseLimit> GeneratePurchaseLimits(List<Show> shows, List<ShowSession> showSessions)
        {
            var limits = new List<PurchaseLimit>();
            var channels = new[] { "WEB", "APP", "MINI_PROGRAM", null };
            var userTypes = new[] { "NORMAL", "MEMBER", "VIP", null };
            var limitTypes = new[] { "TICKET", "ORDER" };

            foreach (var show in shows.Take(Math.Min(5, shows.Count)))
            {
                limits.Add(new PurchaseLimit
                {
                    LimitName = $"{show.ShowName}限购策略",
                    ShowId = show.ShowId,
                    SessionId = null,
                    Channel = channels[_random.Next(channels.Length)],
                    UserType = userTypes[_random.Next(userTypes.Length)],
                    MaxBuyCount = _random.Next(2, 6),
                    LimitType = limitTypes[_random.Next(limitTypes.Length)],
                    StartTime = null,
                    EndTime = null,
                    Status = "ENABLED"
                });
            }

            foreach (var session in showSessions.OrderBy(x => Guid.NewGuid()).Take(Math.Min(10, showSessions.Count)))
            {
                limits.Add(new PurchaseLimit
                {
                    LimitName = $"场次限购-{session.SessionId}",
                    ShowId = null,
                    SessionId = session.SessionId,
                    Channel = channels[_random.Next(channels.Length)],
                    UserType = userTypes[_random.Next(userTypes.Length)],
                    MaxBuyCount = _random.Next(2, 8),
                    LimitType = limitTypes[_random.Next(limitTypes.Length)],
                    StartTime = session.SaleStartTime,
                    EndTime = session.SaleEndTime,
                    Status = _random.Next(0, 3) == 0 ? "DISABLED" : "ENABLED"
                });
            }

            return limits;
        }

        #endregion

        #region User & Permission Data Generation

        /// <summary>幂等创建基础角色：USER / OPERATOR / Admin（按 RoleCode 去重）</summary>
        private List<Role> GenerateRoles()
        {
            var roleDefs = new[]
            {
                (Code: "USER", Name: "普通用户", Desc: "默认注册角色，可购票"),
                (Code: "OPERATOR", Name: "运营人员", Desc: "演出/场次/订单运营权限"),
                (Code: "Admin", Name: "系统管理员", Desc: "全部管理权限")
            };

            var existing = _context.Set<Role>().ToList();
            var existingCodes = existing.Select(r => r.RoleCode).ToHashSet();
            var roles = new List<Role>(existing);

            foreach (var (code, name, desc) in roleDefs)
            {
                if (existingCodes.Contains(code)) continue;

                var role = new Role
                {
                    RoleCode = code,
                    RoleName = name,
                    RoleDesc = desc,
                    Status = true,
                    CreateBy = DefaultActor,
                    UpdateBy = DefaultActor
                };
                _context.Set<Role>().Add(role);
                roles.Add(role);
            }

            _context.SaveChanges();
            return roles;
        }

        /// <summary>幂等创建测试用户（admin + 3 个普通用户），附角色与已实名认证信息</summary>
        private List<SysUser> GenerateUsers(List<Role> roles)
        {
            var existingNames = _context.Set<SysUser>().Select(u => u.UserName).ToHashSet();
            var passwordHasher = new PasswordHasher<SysUser>();
            var users = new List<SysUser>();

            var userDefs = new[]
            {
                (Name: AdminUserName, Nick: "系统管理员", Phone: "13900000001", Email: "admin@showtime.test", Roles: new[] { "Admin", "USER" }),
                (Name: "testuser1", Nick: "测试用户一", Phone: "13900000002", Email: "testuser1@showtime.test", Roles: new[] { "USER" }),
                (Name: "testuser2", Nick: "测试用户二", Phone: "13900000003", Email: "testuser2@showtime.test", Roles: new[] { "USER" }),
                (Name: "testuser3", Nick: "测试用户三", Phone: "13900000004", Email: "testuser3@showtime.test", Roles: new[] { "USER" })
            };

            foreach (var def in userDefs)
            {
                if (existingNames.Contains(def.Name)) continue;

                var user = new SysUser
                {
                    UserName = def.Name,
                    Nickname = def.Nick,
                    Phone = def.Phone,
                    Email = def.Email,
                    UserType = "NORMAL",
                    Status = 1,
                    PasswordHash = string.Empty,
                    CreateBy = DefaultActor,
                    UpdateBy = DefaultActor
                };
                user.PasswordHash = passwordHasher.HashPassword(
                    user,
                    def.Name == AdminUserName ? AdminPassword : TestUserPassword);

                foreach (var roleCode in def.Roles)
                {
                    var role = roles.First(r => r.RoleCode == roleCode);
                    user.UserRoles.Add(new UserRole { Role = role });
                }

                // 每个测试用户配一条已实名认证记录（订单流程支持按实名购票）
                user.RealNames.Add(new UserRealName
                {
                    RealName = _faker.Name.FullName(),
                    IdCardNo = GenerateIdCardNo(),
                    IsDefault = true,
                    IsVerified = true,
                    CreateBy = DefaultActor,
                    UpdateBy = DefaultActor
                });

                _context.Set<SysUser>().Add(user);
                users.Add(user);
            }

            _context.SaveChanges();
            return users;
        }

        /// <summary>幂等创建权限树与角色-权限映射</summary>
        private void GeneratePermissions(List<Role> roles)
        {
            // (permCode, permName, resourceType, parentCode)  resourceType 对齐 DDL CK_PERMISSION_TYPE: MENU/BUTTON/API/DATA
            var permDefs = new[]
            {
                ("system:manage", "系统管理", "MENU", (string?)null),
                ("user:manage", "用户管理", "MENU", "system:manage"),
                ("user:view", "用户查询", "API", "user:manage"),
                ("user:edit", "用户编辑", "API", "user:manage"),
                ("role:manage", "角色管理", "MENU", "system:manage"),
                ("role:view", "角色查询", "API", "role:manage"),
                ("role:edit", "角色编辑", "API", "role:manage"),
                ("show:manage", "演出管理", "MENU", null),
                ("show:create", "演出创建", "API", "show:manage"),
                ("show:edit", "演出编辑", "API", "show:manage"),
                ("show:publish", "演出发布/审核", "API", "show:manage"),
                ("session:manage", "场次管理", "MENU", null),
                ("session:create", "场次排布", "API", "session:manage"),
                ("session:status", "场次状态变更", "API", "session:manage"),
                ("seat:manage", "座位管理", "MENU", null),
                ("seat:edit", "座位图编辑", "API", "seat:manage"),
                ("order:manage", "订单管理", "MENU", null),
                ("order:view", "订单查询", "API", "order:manage"),
                ("order:refund", "订单退款", "API", "order:manage")
            };

            var existingCodes = _context.Set<Permission>().Select(p => p.PermCode).ToHashSet();
            var created = new List<Permission>();
            int sort = 10;

            // 第一阶段：先落库全部权限（此时 ParentId 留空），
            // 避免新实体 PermissionId 尚未生成（仍为 0）导致外键指向 0
            foreach (var (code, name, resourceType, _) in permDefs)
            {
                if (existingCodes.Contains(code)) continue;

                var perm = new Permission
                {
                    PermCode = code,
                    PermName = name,
                    ResourceType = resourceType,
                    ParentId = null,
                    SortOrder = sort += 10,
                    Status = true,
                    CreateBy = DefaultActor,
                    UpdateBy = DefaultActor
                };
                _context.Set<Permission>().Add(perm);
                created.Add(perm);
            }

            _context.SaveChanges();

            // 第二阶段：回填父子关系（父权限此刻已有真实 PermissionId）
            var byCode = _context.Set<Permission>().ToDictionary(p => p.PermCode);
            foreach (var (code, _, _, parentCode) in permDefs)
            {
                if (parentCode is null || !byCode.TryGetValue(code, out var perm)) continue;
                if (!byCode.TryGetValue(parentCode, out var parent)) continue;

                perm.ParentId = parent.PermissionId;
                _context.Set<Permission>().Update(perm);
            }

            _context.SaveChanges();

            // 角色-权限映射（幂等）
            var allPermissions = _context.Set<Permission>().ToList();
            var rolePerms = _context.Set<RolePermission>().ToList();
            var existingKeys = rolePerms
                .Select(rp => $"{rp.RoleId}_{rp.PermissionId}")
                .ToHashSet();

            void Grant(Role role, string permCode)
            {
                var perm = allPermissions.First(p => p.PermCode == permCode);
                var key = $"{role.RoleId}_{perm.PermissionId}";
                if (existingKeys.Contains(key)) return;
                _context.Set<RolePermission>().Add(new RolePermission
                {
                    Role = role,
                    Permission = perm
                });
                existingKeys.Add(key);
            }

            var admin = roles.First(r => r.RoleCode == "Admin");
            var operatorRole = roles.First(r => r.RoleCode == "OPERATOR");
            var userRole = roles.First(r => r.RoleCode == "USER");

            foreach (var perm in allPermissions)
            {
                Grant(admin, perm.PermCode);
            }
            Grant(operatorRole, "show:manage");
            Grant(operatorRole, "show:create");
            Grant(operatorRole, "show:edit");
            Grant(operatorRole, "show:publish");
            Grant(operatorRole, "session:manage");
            Grant(operatorRole, "session:create");
            Grant(operatorRole, "session:status");
            Grant(operatorRole, "order:view");
            Grant(userRole, "user:view");

            _context.SaveChanges();
        }

        private bool HasExistingShowData() =>
            _context.Set<Category>().Count() > 0 ||
            _context.Set<SeatMap>().Count() > 0 ||
            _context.Set<Venue>().Count() > 0;

        /// <summary>生成 18 位大陆身份证号码（测试数据，不保证校验位合法）</summary>
        private string GenerateIdCardNo()
        {
            var region = _faker.PickRandom(new[] { "110101", "310101", "440101", "510101", "330101", "420101" });
            var birth = new DateTime(_random.Next(1970, 2005), _random.Next(1, 13), _random.Next(1, 29));
            var seq = _random.Next(0, 999).ToString("D3");
            return region + birth.ToString("yyyyMMdd") + seq + _faker.PickRandom("0123456789X");
        }

        #endregion

        #region Utility Methods

        private void Log(string message)
        {
            if (_enableDetailedLog || message.Contains("ERROR") || message.Contains("Completed"))
            {
                Console.WriteLine(message);
            }
        }

        private void PrintStatistics()
        {
            Console.WriteLine();
            Console.WriteLine("=== Data Generation Statistics ===");
            Console.WriteLine($"  ROLE:              {_context.Set<Role>().Count()}");
            Console.WriteLine($"  SYS_USER:          {_context.Set<SysUser>().Count()}");
            Console.WriteLine($"  USER_ROLE:         {_context.Set<UserRole>().Count()}");
            Console.WriteLine($"  PERMISSION:        {_context.Set<Permission>().Count()}");
            Console.WriteLine($"  ROLE_PERMISSION:   {_context.Set<RolePermission>().Count()}");
            Console.WriteLine($"  USER_REAL_NAME:    {_context.Set<UserRealName>().Count()}");
            Console.WriteLine($"  CATEGORY:          {_context.Set<Category>().Count()}");
            Console.WriteLine($"  TAG:               {_context.Set<Tag>().Count()}");
            Console.WriteLine($"  VENUE:             {_context.Set<Venue>().Count()}");
            Console.WriteLine($"  SEAT_MAP:          {_context.Set<SeatMap>().Count()}");
            Console.WriteLine($"  SEAT_SECTION:      {_context.Set<SeatSection>().Count()}");
            Console.WriteLine($"  SEAT:              {_context.Set<Seat>().Count()}");
            Console.WriteLine($"  SHOW:              {_context.Set<Show>().Count()}");
            Console.WriteLine($"  SHOW_TAG:          {_context.Set<ShowTag>().Count()}");
            Console.WriteLine($"  SHOW_SESSION:      {_context.Set<ShowSession>().Count()}");
            Console.WriteLine($"  PRICE_STRATEGY:    {_context.Set<PriceStrategy>().Count()}");
            Console.WriteLine($"  PURCHASE_LIMIT:    {_context.Set<PurchaseLimit>().Count()}");
            Console.WriteLine("================================================");
        }

        #endregion
    }
}
