using System;
using System.Collections.Generic;
using System.Linq;
using Bogus;
using Microsoft.EntityFrameworkCore;
using ShowtimeBackend.Data;
using ShowtimeBackend.Entities.ShowSession;
using ShowtimeBackend.Entities.SeatZone;

namespace ShowtimeBackend.TestData
{
    public class TestDataGenerator
    {
        private readonly AppDbContext _context;
        private readonly Random _random = new Random();
        private readonly Faker _faker = new Faker("zh_CN");

        private const int SHOW_COUNT = 10;
        private const int MIN_SESSIONS = 3;
        private const int MAX_SESSIONS = 5;
        private const int SEATS_PER_SESSION = 200;

        public TestDataGenerator(AppDbContext context)
        {
            _context = context;
        }

        public void GenerateAllData()
        {
            Console.WriteLine("=== Starting Test Data Generation ===");

            using var transaction = _context.Database.BeginTransaction();

            try
            {
                // 1. 生成分类
                var categories = GenerateCategories();
                _context.Set<Category>().AddRange(categories);
                _context.SaveChanges();
                Console.WriteLine($"  [1/10] Generated {categories.Count} categories");

                // 2. 生成标签
                var tags = GenerateTags();
                _context.Set<Tag>().AddRange(tags);
                _context.SaveChanges();
                Console.WriteLine($"  [2/10] Generated {tags.Count} tags");

                // 3. 生成场馆
                var venues = GenerateVenues();
                _context.Set<Venue>().AddRange(venues);
                _context.SaveChanges();
                Console.WriteLine($"  [3/10] Generated {venues.Count} venues");

                // 4. 生成座位图
                var seatMaps = GenerateSeatMaps(venues);
                _context.Set<SeatMap>().AddRange(seatMaps);
                _context.SaveChanges();
                Console.WriteLine($"  [4/10] Generated {seatMaps.Count} seat maps");

                // 5. 生成座位区域
                var seatSections = GenerateSeatSections(seatMaps);
                _context.Set<SeatSection>().AddRange(seatSections);
                _context.SaveChanges();
                Console.WriteLine($"  [5/10] Generated {seatSections.Count} seat sections");

                // 6. 生成座位
                var seats = GenerateSeats(seatSections);
                _context.Set<Seat>().AddRange(seats);
                _context.SaveChanges();
                Console.WriteLine($"  [6/10] Generated {seats.Count} seats");

                // 7. 生成演出
                var shows = GenerateShows(categories);
                _context.Set<Show>().AddRange(shows);
                _context.SaveChanges();
                Console.WriteLine($"  [7/10] Generated {shows.Count} shows");

                // 8. 生成演出标签关联
                var showTags = GenerateShowTags(shows, tags);
                _context.Set<ShowTag>().AddRange(showTags);
                _context.SaveChanges();
                Console.WriteLine($"  [8/10] Generated {showTags.Count} show-tag associations");

                // 9. 生成场次
                var showSessions = GenerateShowSessions(shows, seatMaps);
                _context.Set<ShowSession>().AddRange(showSessions);
                _context.SaveChanges();
                Console.WriteLine($"  [9/10] Generated {showSessions.Count} show sessions");

                // 10. 生成票价策略
                var priceStrategies = GeneratePriceStrategies(showSessions, seatSections);
                _context.Set<PriceStrategy>().AddRange(priceStrategies);
                _context.SaveChanges();
                Console.WriteLine($"  [10/11] Generated {priceStrategies.Count} price strategies");

                // 11. 生成限购策略
                var purchaseLimits = GeneratePurchaseLimits(shows, showSessions);
                _context.Set<PurchaseLimit>().AddRange(purchaseLimits);
                _context.SaveChanges();
                Console.WriteLine($"  [11/11] Generated {purchaseLimits.Count} purchase limits");

                transaction.Commit();

                Console.WriteLine("=== Test Data Generation Completed ===");
                PrintStatistics();
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                Console.WriteLine($"Error: {ex.Message}");
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
                    int rows = _random.Next(10, 20);
                    int cols = _random.Next(10, 15);
                    seatMaps.Add(new SeatMap
                    {
                        VenueId = venue.VenueId,
                        MapName = $"座位图{(char)('A' + i)}",
                        RowCount = rows,
                        ColCount = cols,
                        Status = "ENABLED"
                    });
                }
            }

            if (seatMaps.Count == 0 && venues.Any())
            {
                seatMaps.Add(new SeatMap
                {
                    VenueId = venues.First().VenueId,
                    MapName = "默认座位图",
                    RowCount = 20,
                    ColCount = 15,
                    Status = "ENABLED"
                });
            }

            return seatMaps;
        }

        private List<SeatSection> GenerateSeatSections(List<SeatMap> seatMaps)
        {
            var sections = new List<SeatSection>();
            var sectionConfigs = new[]
            {
                ("VIP区", "#E74C3C", 1.5m),
                ("A区", "#3498DB", 1.2m),
                ("B区", "#2ECC71", 1.0m),
                ("C区", "#F39C12", 0.85m),
                ("D区", "#9B59B6", 0.7m),
                ("包厢", "#1ABC9C", 2.0m)
            };

            foreach (var seatMap in seatMaps)
            {
                int sectionCount = _random.Next(3, Math.Min(6, seatMap.RowCount / 2 + 1));
                int rowsPerSection = seatMap.RowCount / sectionCount;

                for (int i = 0; i < sectionCount && i < sectionConfigs.Length; i++)
                {
                    var (name, color, factor) = sectionConfigs[i];

                    int rowStart = (i * rowsPerSection) + 1;
                    int rowEnd = (i == sectionCount - 1) ? seatMap.RowCount : (i + 1) * rowsPerSection;

                    int colStart = 1;
                    int colEnd = seatMap.ColCount;

                    if (rowStart > seatMap.RowCount) continue;

                    sections.Add(new SeatSection
                    {
                        SeatMapId = seatMap.SeatMapId,
                        SectionName = name,
                        SectionCode = $"{(char)('A' + i)}",
                        RowStart = rowStart,
                        RowEnd = Math.Min(rowEnd, seatMap.RowCount),
                        ColStart = colStart,
                        ColEnd = colEnd,
                        Color = color,
                        PriceFactor = Math.Round(factor + (decimal)(_random.NextDouble() * 0.2 - 0.1), 2),
                        Status = "ENABLED"
                    });
                }

                if (!sections.Any(s => s.SeatMapId == seatMap.SeatMapId))
                {
                    sections.Add(new SeatSection
                    {
                        SeatMapId = seatMap.SeatMapId,
                        SectionName = "标准区",
                        SectionCode = "A",
                        RowStart = 1,
                        RowEnd = seatMap.RowCount,
                        ColStart = 1,
                        ColEnd = seatMap.ColCount,
                        Color = "#3498DB",
                        PriceFactor = 1.0m,
                        Status = "ENABLED"
                    });
                }
            }
            return sections;
        }

        private List<Seat> GenerateSeats(List<SeatSection> seatSections)
        {
            var seats = new List<Seat>();
            int totalSeatsGenerated = 0;
            int targetSeats = SHOW_COUNT * MAX_SESSIONS * SEATS_PER_SESSION;

            foreach (var section in seatSections)
            {
                int totalRows = section.RowEnd - section.RowStart + 1;
                int totalCols = section.ColEnd - section.ColStart + 1;

                for (int row = section.RowStart; row <= section.RowEnd; row++)
                {
                    for (int col = section.ColStart; col <= section.ColEnd; col++)
                    {
                        string seatNumber = $"{(char)('A' + row - 1)}{col}";
                        seats.Add(new Seat
                        {
                            SeatSectionId = section.SeatSectionId,
                            SeatNumber = seatNumber,
                            RowNumber = row,
                            ColNumber = col,
                            Status = _random.Next(0, 5) == 0 ? "DISABLED" : "ENABLED"
                        });
                        totalSeatsGenerated++;
                    }
                }
            }

            Console.WriteLine($"  Generated {totalSeatsGenerated} seats across all sections");
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
            var shuffledNames = showNames.OrderBy(x => Guid.NewGuid()).Take(SHOW_COUNT).ToList();

            for (int i = 0; i < SHOW_COUNT; i++)
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
                int sessionCount = _random.Next(MIN_SESSIONS, MAX_SESSIONS + 1);
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
                        var basePrice = 80 + (section.PriceFactor * 100) + (_random.Next(0, 20) * 5);
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

        #region Utility Methods

        private void PrintStatistics()
        {
            Console.WriteLine("\n=== Data Generation Statistics ===");
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