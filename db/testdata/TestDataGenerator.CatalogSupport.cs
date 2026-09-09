using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using ShowtimeBackend.Entities.OrderTicket;
using ShowtimeBackend.Entities.SeatZone;
using ShowtimeBackend.Entities.ShowSession;

namespace ShowtimeBackend.TestData
{
    /// <summary>
    /// 支撑业务数据：营销内容、动态调价规则、选座规则与适用范围、退票/改签策略。
    /// 全部采用“目标表为空才生成”，重复执行不会翻倍。
    /// </summary>
    public partial class TestDataGenerator
    {
        /// <summary>为演出补充营销内容（公告/广告/促销）</summary>
        private int GenerateMarketingContents(IReadOnlyCollection<Show> shows)
        {
            if (_context.Set<MarketingContent>().Count() > 0)
            {
                return 0;
            }

            var list = shows.ToList();
            if (list.Count == 0)
            {
                return 0;
            }

            var templates = new[]
            {
                (Type: "NOTICE", Title: "观演须知与入场提示", Body: "请提前 60 分钟到场，配合安检并出示电子票二维码。迟到观众请在工作人员引导下于幕间入场。"),
                (Type: "NOTICE", Title: "演出时长及中场休息说明", Body: "本场演出时长约 120 分钟，含 15 分钟中场休息，请合理安排出行时间。"),
                (Type: "AD", Title: "会员购票享 9 折", Body: "注册会员下单即享票价 9 折优惠（限原价票，早鸟票不叠加）。"),
                (Type: "PROMOTION", Title: "早鸟票限时开售", Body: "开演前 30 天开放早鸟票，数量有限，售完即止。"),
                (Type: "PROMOTION", Title: "双人套票立减 80 元", Body: "购买同场次任意 2 张票，订单立减 80 元，可与会员折扣同享。"),
                (Type: "NOTICE", Title: "儿童入场政策", Body: "1.2 米以下儿童谢绝入场（亲子场除外），1.2 米以上儿童需凭票入场。")
            };

            var contents = new List<MarketingContent>();
            foreach (var show in list)
            {
                if (show.Status != "PUBLISHED")
                {
                    continue;
                }

                int count = _random.Next(1, 4);
                var picks = templates.OrderBy(_ => Guid.NewGuid()).Take(count).ToList();
                for (int i = 0; i < picks.Count; i++)
                {
                    bool enabled = _random.Next(0, 6) != 0;
                    contents.Add(new MarketingContent
                    {
                        ShowId = show.ShowId,
                        ContentType = picks[i].Type,
                        Title = picks[i].Title,
                        ContentText = picks[i].Body,
                        ImageUrl = picks[i].Type == "NOTICE"
                            ? null
                            : $"https://picsum.photos/seed/mkt{show.ShowId}{i}/800/400",
                        SortOrder = i,
                        Status = enabled ? "ENABLED" : "DISABLED",
                        PublishTime = enabled ? DateTime.UtcNow.AddDays(-_random.Next(0, 30)) : null,
                        CreateBy = DefaultActor,
                        UpdateBy = DefaultActor
                    });
                }
            }

            if (contents.Count > 0)
            {
                _context.Set<MarketingContent>().AddRange(contents);
                _context.SaveChanges();
            }

            return contents.Count;
        }

        /// <summary>为可售卖的场次生成动态调价规则（TIME_WINDOW / INVENTORY_RATE）</summary>
        private int GenerateDynamicPricingRules(
            IReadOnlyCollection<ShowSession> sessions,
            IReadOnlyCollection<SeatSection> sections)
        {
            if (_context.Set<DynamicPricingRule>().Count() > 0)
            {
                return 0;
            }

            var usableSessions = sessions
                .Where(s => s.SessionStatus is "ONSALE" or "PRESALE" or "UPCOMING")
                .ToList();
            if (usableSessions.Count == 0)
            {
                return 0;
            }

            var sectionList = sections.ToList();
            var rules = new List<DynamicPricingRule>();

            // 按场次生成 2~4 条“越临近开演折扣越小”的规则，另给部分 VIP 区加固定价规则
            foreach (var session in usableSessions.OrderBy(_ => Guid.NewGuid()).Take(Math.Min(30, usableSessions.Count)))
            {
                var windowRules = new[]
                {
                    new { Name = "早鸟 9 折（开演前 30 天以上）", Start = -30 * 24 * 60, End = -15 * 24 * 60, Adj = "DISCOUNT_RATE", Val = 0.90m },
                    new { Name = "常规票价（开演前 15~3 天）", Start = -15 * 24 * 60, End = -3 * 24 * 60, Adj = "DISCOUNT_RATE", Val = 1.00m },
                    new { Name = "临场加价（开演前 3 天内）", Start = -3 * 24 * 60, End = 0, Adj = "DISCOUNT_RATE", Val = 1.10m },
                    new { Name = "热门场次优惠满减", Start = -7 * 24 * 60, End = -1 * 24 * 60, Adj = "AMOUNT_OFF", Val = 20m }
                };

                int count = _random.Next(2, 4);
                var picks = windowRules.OrderBy(_ => Guid.NewGuid()).Take(count).ToList();
                foreach (var pick in picks)
                {
                    rules.Add(new DynamicPricingRule
                    {
                        SessionId = session.SessionId,
                        SeatSectionId = null,
                        RuleName = pick.Name,
                        TriggerType = "TIME_WINDOW",
                        StartOffsetMinutes = pick.Start,
                        EndOffsetMinutes = pick.End,
                        AdjustmentType = pick.Adj,
                        AdjustmentValue = pick.Val,
                        Priority = _random.Next(1, 100),
                        Status = _random.Next(0, 5) == 0 ? "DISABLED" : "ENABLED",
                        CreateBy = DefaultActor,
                        UpdateBy = DefaultActor
                    });
                }

                if (_random.Next(0, 2) == 0 && session.SeatMapId > 0)
                {
                    var vip = sectionList.FirstOrDefault(s =>
                        s.SeatMapId == session.SeatMapId && s.SectionType == "VIP");
                    if (vip is not null)
                    {
                        rules.Add(new DynamicPricingRule
                        {
                            SessionId = session.SessionId,
                            SeatSectionId = vip.SeatSectionId,
                            RuleName = "VIP 区库存紧张自动恢复原价",
                            TriggerType = "INVENTORY_RATE",
                            StartOffsetMinutes = null,
                            EndOffsetMinutes = null,
                            AdjustmentType = "FIXED_PRICE",
                            AdjustmentValue = 580m,
                            Priority = 200,
                            Status = "ENABLED",
                            CreateBy = DefaultActor,
                            UpdateBy = DefaultActor
                        });
                    }
                }
            }

            if (rules.Count > 0)
            {
                _context.Set<DynamicPricingRule>().AddRange(rules);
                _context.SaveChanges();
            }

            return rules.Count;
        }

        /// <summary>生成选座规则及其适用范围（SEAT_RULE / SEAT_RULE_SCOPE）</summary>
        private int GenerateSeatRulesAndScopes(
            IReadOnlyCollection<SeatMap> seatMaps,
            IReadOnlyCollection<SeatSection> sections)
        {
            if (_context.Set<SeatRule>().Count() > 0)
            {
                return 0;
            }

            var maps = seatMaps
                .Where(m => m.MapStatus == "ENABLED")
                .ToList();
            if (maps.Count == 0)
            {
                return 0;
            }

            var mapIds = maps.Select(m => m.SeatMapId).ToHashSet();
            var sectionList = sections.Where(s => mapIds.Contains(s.SeatMapId)).ToList();

            var ruleSpecs = new[]
            {
                new { Code = "RULE_CONNECTED_4", Name = "连座优先规则", Type = "CONTINUOUS", Min = 2, Max = 4, CrossRow = false, CrossSection = false, Priority = 100 },
                new { Code = "RULE_LIMIT_6", Name = "单笔限购 6 张", Type = "LIMIT_COUNT", Min = 1, Max = 6, CrossRow = true, CrossSection = false, Priority = 90 },
                new { Code = "RULE_NO_SINGLE", Name = "避免单座夹心", Type = "NO_SINGLE_LEFT", Min = 1, Max = 4, CrossRow = false, CrossSection = false, Priority = 80 }
            };

            var rules = new List<SeatRule>();
            var scopes = new List<SeatRuleScope>();
            foreach (var spec in ruleSpecs)
            {
                var rule = new SeatRule
                {
                    RuleCode = spec.Code,
                    RuleName = spec.Name,
                    RuleType = spec.Type,
                    MinSeatCount = spec.Min,
                    MaxSeatCount = spec.Max,
                    AllowCrossRow = spec.CrossRow,
                    AllowCrossSection = spec.CrossSection,
                    Priority = spec.Priority,
                    RuleStatus = "ENABLED",
                    Remark = spec.Type == "CONTINUOUS" ? "同排相邻座位优先" : null,
                    CreateBy = DefaultActor,
                    UpdateBy = DefaultActor
                };
                _context.Set<SeatRule>().Add(rule);
                rules.Add(rule);
            }

            _context.SaveChanges();

            foreach (var rule in rules)
            {
                foreach (var map in maps)
                {
                    scopes.Add(new SeatRuleScope
                    {
                        SeatRuleId = rule.SeatRuleId,
                        ScopeType = "MAP",
                        SeatMapId = map.SeatMapId,
                        SeatSectionId = null,
                        ScopeStatus = "ENABLED",
                        CreateBy = DefaultActor,
                        UpdateBy = DefaultActor
                    });
                }
            }

            // VIP 区额外一条“每笔最多 2 张”的票区级规则
            foreach (var vip in sectionList.Where(s => s.SectionType == "VIP").Take(3))
            {
                var rule = new SeatRule
                {
                    RuleCode = $"RULE_VIP_{vip.SeatMapId}_{vip.SectionCode}",
                    RuleName = $"{vip.SectionName}限购 2 张",
                    RuleType = "SECTION_LIMIT",
                    MinSeatCount = 1,
                    MaxSeatCount = 2,
                    AllowCrossRow = false,
                    AllowCrossSection = false,
                    Priority = 70,
                    RuleStatus = "ENABLED",
                    Remark = null,
                    CreateBy = DefaultActor,
                    UpdateBy = DefaultActor
                };
                _context.Set<SeatRule>().Add(rule);
                _context.SaveChanges();

                scopes.Add(new SeatRuleScope
                {
                    SeatRuleId = rule.SeatRuleId,
                    ScopeType = "SECTION",
                    SeatMapId = null,
                    SeatSectionId = vip.SeatSectionId,
                    ScopeStatus = "ENABLED",
                    CreateBy = DefaultActor,
                    UpdateBy = DefaultActor
                });
            }

            _context.Set<SeatRuleScope>().AddRange(scopes);
            _context.SaveChanges();
            return rules.Count + scopes.Count;
        }

        /// <summary>生成退票策略（全局通用 + 少量按演出定制）</summary>
        private int GenerateRefundPolicies(IReadOnlyCollection<Show> shows)
        {
            if (_context.Set<RefundPolicy>().Count() > 0)
            {
                return 0;
            }

            var list = shows.ToList();
            var policies = new List<RefundPolicy>();
            var global = new[]
            {
                new { Name = "开演 48 小时前可全额退款", Deadline = 48, Rate = 1.00m, Fee = 0m, Priority = 10, Remark = "默认策略：开演前 48 小时以上申请全额退款" },
                new { Name = "开演前 24~48 小时退款 80%", Deadline = 24, Rate = 0.80m, Fee = 0m, Priority = 20, Remark = "按票面金额的 80% 退款" },
                new { Name = "开演前 3~24 小时退款 50%", Deadline = 3, Rate = 0.50m, Fee = 0m, Priority = 30, Remark = "按票面金额的 50% 退款" },
                new { Name = "开演前 3 小时内不可退", Deadline = 0, Rate = 0.00m, Fee = 0m, Priority = 40, Remark = "临近开演不接受退票" }
            };
            foreach (var p in global)
            {
                policies.Add(new RefundPolicy
                {
                    ShowId = null,
                    PolicyName = p.Name,
                    RefundDeadlineHour = p.Deadline,
                    RefundRate = p.Rate,
                    ServiceFee = p.Fee,
                    Priority = p.Priority,
                    Status = 1,
                    Remark = p.Remark,
                    CreateBy = DefaultActor,
                    UpdateBy = DefaultActor
                });
            }

            foreach (var show in list.Where(s => s.Status == "PUBLISHED").Take(3))
            {
                policies.Add(new RefundPolicy
                {
                    ShowId = show.ShowId,
                    PolicyName = $"{show.ShowName}专享：开演前 24 小时免费退",
                    RefundDeadlineHour = 24,
                    RefundRate = 1.00m,
                    ServiceFee = 0m,
                    Priority = 5,
                    Status = 1,
                    Remark = "演出方定制退票策略",
                    CreateBy = DefaultActor,
                    UpdateBy = DefaultActor
                });
            }

            _context.Set<RefundPolicy>().AddRange(policies);
            _context.SaveChanges();
            return policies.Count;
        }

        /// <summary>生成改签策略（全局通用 + 少量按演出定制）</summary>
        private int GenerateExchangePolicies(IReadOnlyCollection<Show> shows)
        {
            if (_context.Set<ExchangePolicy>().Count() > 0)
            {
                return 0;
            }

            var list = shows.ToList();
            var policies = new List<ExchangePolicy>
            {
                new()
                {
                    ShowId = null,
                    PolicyName = "开演前 24 小时可免费改签一次",
                    ExchangeDeadlineHour = 24,
                    ExchangeFee = 0m,
                    AllowCrossSession = 1,
                    Priority = 10,
                    Status = 1,
                    Remark = "默认策略：同演出内可改签至其他场次"
                },
                new()
                {
                    ShowId = null,
                    PolicyName = "开演前 2~24 小时改签收取 10 元手续费",
                    ExchangeDeadlineHour = 2,
                    ExchangeFee = 10m,
                    AllowCrossSession = 1,
                    Priority = 20,
                    Status = 1,
                    Remark = "临近开演改签收取手续费"
                }
            };

            foreach (var show in list.Where(s => s.Status == "PUBLISHED").Take(3))
            {
                policies.Add(new ExchangePolicy
                {
                    ShowId = show.ShowId,
                    PolicyName = $"{show.ShowName}专享：任意场次可改签",
                    ExchangeDeadlineHour = 6,
                    ExchangeFee = 5m,
                    AllowCrossSession = 1,
                    Priority = 5,
                    Status = 1,
                    Remark = "演出方定制改签策略",
                    CreateBy = DefaultActor,
                    UpdateBy = DefaultActor
                });
            }

            _context.Set<ExchangePolicy>().AddRange(policies);
            _context.SaveChanges();
            return policies.Count;
        }
    }
}
