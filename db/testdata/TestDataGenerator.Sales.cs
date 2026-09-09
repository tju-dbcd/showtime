using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using ShowtimeBackend.Entities.OrderTicket;
using ShowtimeBackend.Entities.SeatZone;
using ShowtimeBackend.Entities.ShowSession;
using ShowtimeBackend.Entities.UserPermission;

namespace ShowtimeBackend.TestData
{
    public partial class TestDataGenerator
    {
        private sealed record SalesStats(
            int Orders,
            int OrderItems,
            int Payments,
            int ETickets,
            int Refunds,
            int Exchanges,
            int SeatLocks,
            int Reservations);

        private sealed class OrderSeedRow
        {
            public required Order Order { get; init; }
            public required ShowSession Session { get; init; }
            public required string Kind { get; set; }
            public required bool ExchangedCandidate { get; set; }
        }

        /// <summary>
        /// 生成订单/支付/电子票/锁座/退票/改签等交易数据。
        /// 幂等策略：只要 T_ORDER 中已有订单就不再生成，防止重复执行导致订单号/座位冲突。
        /// </summary>
        private SalesStats GenerateSalesData(
            IReadOnlyCollection<Show> shows,
            IReadOnlyCollection<ShowSession> sessions,
            IReadOnlyCollection<SeatMap> seatMaps,
            IReadOnlyCollection<SeatSection> sections,
            IReadOnlyCollection<Seat> seats)
        {
            if (_context.Set<Order>().Count() > 0)
            {
                Log("检测到已存在订单数据，跳过交易数据生成。如需重建请先清空订单/票务相关表。");
                return new SalesStats(0, 0, 0, 0, 0, 0, 0, 0);
            }

            var sessionList = sessions.ToList();
            var sectionList = sections.ToList();
            var seatList = seats.ToList();
            if (sessionList.Count == 0 || seatList.Count == 0)
            {
                Log("场次/座位数据为空，跳过交易数据生成。");
                return new SalesStats(0, 0, 0, 0, 0, 0, 0, 0);
            }

            var now = DateTime.UtcNow;
            var buyers = LoadBuyers();
            if (buyers.Count == 0)
            {
                Log("没有可下单的已实名用户，跳过交易数据生成。");
                return new SalesStats(0, 0, 0, 0, 0, 0, 0, 0);
            }

            var priceStrategyList = _context.Set<PriceStrategy>().ToList();
            var seatBySection = seatList
                .GroupBy(s => s.SeatSectionId)
                .ToDictionary(g => g.Key, g => g.ToList());

            var usedSeatBySession = new Dictionary<long, HashSet<long>>();
            var orderRows = new List<OrderSeedRow>();
            var generatedOrderItems = 0;

            // 1. 按场次生成“售出席位”并聚合成订单
            foreach (var session in sessionList)
            {
                string status = session.SessionStatus;
                if (status is "UPCOMING" or "PRESALE")
                {
                    continue; // 未开售/未开演场次不生成历史订单，保留完整余座供前端演示
                }

                var strategiesForSession = priceStrategyList
                    .Where(p => p.SessionId == session.SessionId && p.Status == "ENABLED")
                    .ToList();
                if (strategiesForSession.Count == 0)
                {
                    continue;
                }

                var strategyBySection = strategiesForSession
                    .GroupBy(p => p.SeatSectionId)
                    .ToDictionary(g => g.Key, g => PickPriceStrategy(g.ToList()));

                var usableSections = sectionList
                    .Where(s => s.SeatMapId == session.SeatMapId && s.IsSellable &&
                                strategyBySection.ContainsKey(s.SeatSectionId))
                    .ToList();
                if (usableSections.Count == 0)
                {
                    continue;
                }

                var usableSeats = usableSections
                    .SelectMany(s => seatBySection.TryGetValue(s.SeatSectionId, out var list) ? list : new List<Seat>())
                    .Where(s => s.SeatStatus == "ENABLED" && s.IsSellable)
                    .OrderBy(_ => Guid.NewGuid())
                    .ToList();
                if (usableSeats.Count == 0)
                {
                    continue;
                }

                double ratio = status switch
                {
                    "SOLD_OUT" => 1.0,
                    "ENDED" => _endedSessionSoldRatio,
                    _ => _onSaleSessionSoldRatio
                };
                int soldCount = Math.Max(0, (int)Math.Round(usableSeats.Count * ratio));
                soldCount = Math.Min(soldCount, usableSeats.Count);

                var chosen = usableSeats.Take(soldCount).ToList();
                var sessionUsed = usedSeatBySession.GetValueOrDefault(session.SessionId)
                    ?? new HashSet<long>();
                foreach (var seat in chosen)
                {
                    sessionUsed.Add(seat.SeatId);
                }
                usedSeatBySession[session.SessionId] = sessionUsed;

                // 随机 1~4 张为一单，尽量模拟真实购票行为
                int index = 0;
                while (index < chosen.Count)
                {
                    int chunkSize = Math.Min(_random.Next(1, 5), chosen.Count - index);
                    var chunk = chosen.Skip(index).Take(chunkSize).ToList();
                    index += chunkSize;

                    var buyer = buyers[_random.Next(buyers.Count)];
                    string kind = PickOrderKind(status, chunk.Count);
                    var order = new Order
                    {
                        OrderNo = CreateBusinessNo("ORD", now),
                        UserId = buyer.UserId,
                        SessionId = session.SessionId,
                        OrderType = "NORMAL",
                        ParentOrderId = null,
                        TotalAmount = 0m,
                        DiscountAmount = 0m,
                        TicketCount = chunk.Count,
                        OrderStatus = kind,
                        ExpireTime = now.AddMinutes(15),
                        PayTime = null,
                        IssueTime = null,
                        CancelTime = null,
                        Source = _faker.PickRandom(new[] { "WEB", "WEB", "WEB", "APP", "APP", "MINI_PROGRAM" }),
                        IpAddress = GenerateIp(),
                        Remark = _random.Next(0, 10) == 0 ? "测试数据生成" : null,
                        IdempotencyKey = null,
                        IdempotencyRequestHash = null,
                        CreateBy = buyer.UserName,
                        UpdateBy = buyer.UserName
                    };

                    foreach (var seat in chunk)
                    {
                        var strategy = strategyBySection[seat.SeatSectionId];
                        order.Items.Add(new OrderItem
                        {
                            SeatId = seat.SeatId,
                            PriceStrategyId = strategy.PriceStrategyId,
                            RealNameId = PickRealNameId(buyer),
                            UnitPrice = strategy.Price,
                            ItemStatus = "NORMAL",
                            CreateBy = buyer.UserName,
                            UpdateBy = buyer.UserName
                        });
                    }

                    order.TotalAmount = order.Items.Sum(i => i.UnitPrice);
                    orderRows.Add(new OrderSeedRow
                    {
                        Order = order,
                        Session = session,
                        Kind = kind,
                        ExchangedCandidate = false
                    });
                    generatedOrderItems += chunk.Count;
                }
            }

            if (orderRows.Count == 0)
            {
                Log("没有可生成订单的场次/座位组合。");
                return new SalesStats(0, 0, 0, 0, 0, 0, 0, 0);
            }

            // 2. 落库订单与明细（此时取得自增 ID）
            foreach (var row in orderRows)
            {
                _context.Set<Order>().Add(row.Order);
            }
            _context.SaveChanges();

            // 3. 支付流水（除待支付/取消订单外都成功支付）
            var payments = new List<Payment>();
            foreach (var row in orderRows)
            {
                if (row.Order.OrderStatus is "CANCELLED" or "PENDING_PAY")
                {
                    continue;
                }

                payments.Add(new Payment
                {
                    PaymentNo = CreateBusinessNo("PAY", now),
                    OrderId = row.Order.OrderId,
                    UserId = row.Order.UserId,
                    PayAmount = row.Order.TotalAmount,
                    PayChannel = _faker.PickRandom(new[] { "ALIPAY", "WECHAT", "WECHAT", "UNIONPAY", "BALANCE" }),
                    PayStatus = "SUCCESS",
                    TradeNo = CreateBusinessNo("MOCK", now),
                    CallbackData = $"{{\"notify\":true,\"result\":\"SUCCESS\",\"orderId\":{row.Order.OrderId}}}",
                    CallbackTime = now.AddSeconds(-_random.Next(1, 60)),
                    PayTime = now.AddMinutes(-_random.Next(1, 30)),
                    RefundAmount = 0m,
                    CreateBy = row.Order.CreateBy,
                    UpdateBy = row.Order.UpdateBy
                });
            }
            _context.Set<Payment>().AddRange(payments);
            _context.SaveChanges();

            // 4. 锁座记录（每单明细一条；未支付订单 ACTIVE，取消 RELEASED，其余 CONVERTED）
            var locks = new List<SeatLock>();
            foreach (var row in orderRows)
            {
                foreach (var item in row.Order.Items)
                {
                    string lockStatus = row.Kind switch
                    {
                        "PENDING_PAY" => "ACTIVE",
                        "CANCELLED" => "RELEASED",
                        _ => "CONVERTED"
                    };
                    // 锁期与后端 Redis:SeatLockTtlSeconds 默认 600 秒保持一致；
                    // RELEASE_TIME 必须 >= LOCK_TIME（CK_SEAT_LOCK_RELEASE_TIME）
                    var lockTime = lockStatus == "ACTIVE"
                        ? now.AddMinutes(-_random.Next(1, 5))
                        : now.AddMinutes(-_random.Next(5, 240));
                    var expireTime = lockTime.AddMinutes(10);
                    locks.Add(new SeatLock
                    {
                        SessionId = row.Session.SessionId,
                        SeatId = item.SeatId,
                        UserId = row.Order.UserId,
                        LockToken = Guid.NewGuid().ToString("N"),
                        LockStatus = lockStatus,
                        LockTime = lockTime,
                        ExpireTime = expireTime,
                        ReleaseTime = lockStatus is "RELEASED" or "CONVERTED"
                            ? lockTime.AddMinutes(_random.Next(1, 9))
                            : null,
                        Remark = lockStatus == "ACTIVE" ? "等待支付" : null,
                        CreateBy = row.Order.CreateBy,
                        UpdateBy = row.Order.UpdateBy
                    });
                }
            }
            _context.Set<SeatLock>().AddRange(locks);
            _context.SaveChanges();

            // 5. 占座/预留记录：锁座与订单明细一一对应
            var reservations = new List<SeatReservation>();
            var lockQueue = new Queue<SeatLock>(locks);
            foreach (var row in orderRows)
            {
                foreach (var item in row.Order.Items)
                {
                    var lockEntity = lockQueue.Dequeue();
                    bool itemHeld = row.Kind is not ("CANCELLED") &&
                                    (item.ItemStatus == "NORMAL");
                    var reserveTime = now.AddMinutes(-_random.Next(5, 240));
                    reservations.Add(new SeatReservation
                    {
                        SessionId = row.Session.SessionId,
                        SeatId = item.SeatId,
                        OrderItemId = item.OrderItemId,
                        SeatLockId = lockEntity.SeatLockId,
                        ReservationType = "ORDER",
                        ReservationStatus = itemHeld ? "ACTIVE" : "CANCELLED",
                        ReserveTime = reserveTime,
                        CancelTime = itemHeld ? null : reserveTime.AddMinutes(_random.Next(1, 60)),
                        HoldReason = null,
                        Remark = null,
                        CreateBy = row.Order.CreateBy,
                        UpdateBy = row.Order.UpdateBy
                    });
                }
            }
            _context.Set<SeatReservation>().AddRange(reservations);
            _context.SaveChanges();

            // 6. 电子票：已出票订单（ISSUED/PART_REFUND/REFUNDED）按明细出票
            var eTickets = new List<ETicket>();
            foreach (var row in orderRows)
            {
                if (row.Kind is not ("ISSUED" or "PART_REFUND" or "REFUNDED"))
                {
                    continue;
                }

                foreach (var item in row.Order.Items)
                {
                    bool ticketUsed =
                        row.Session.StartTime <= now && item.ItemStatus == "NORMAL" &&
                        _random.Next(0, 100) < 70;
                    string ticketStatus = item.ItemStatus == "REFUNDED"
                        ? "REFUNDED"
                        : ticketUsed
                            ? "USED"
                            : "UNUSED";
                    eTickets.Add(new ETicket
                    {
                        ETicketNo = CreateBusinessNo("TKT", now),
                        OrderItemId = item.OrderItemId,
                        UserId = row.Order.UserId,
                        QrCode = $"SHOWTIME|{row.Order.OrderId}|{item.OrderItemId}|{Guid.NewGuid():N}",
                        AntiFakeCode = Guid.NewGuid().ToString("N")[..20].ToUpperInvariant(),
                        TicketStatus = ticketStatus,
                        CheckTime = ticketStatus == "USED"
                            ? PickCheckTime(row.Session.StartTime, row.Session.EndTime)
                            : null,
                        CheckDevice = ticketStatus == "USED"
                            ? _faker.PickRandom(new[] { "闸机-A01", "手持核销机-03", "闸机-B02" })
                            : null,
                        CheckBy = ticketStatus == "USED" ? "admin" : null,
                        CreateBy = row.Order.CreateBy,
                        UpdateBy = row.Order.UpdateBy
                    });
                }
            }
            _context.Set<ETicket>().AddRange(eTickets);
            _context.SaveChanges();

            // 7. 退票申请（REFUNDED / PART_REFUND）
            int refundCount = CreateRefunds(orderRows, now);

            // 8. 改签（把少量已出票未核销订单做成“已完成的改签”）
            int exchangeCount = CreateExchanges(orderRows, shows, sessionList, sectionList, seatList,
                usedSeatBySession, now);

            int statsOrders = orderRows.Count;
            int statsPayments = payments.Count;
            int statsTickets = eTickets.Count;
            int statsLocks = locks.Count;
            int statsReservations = reservations.Count;

            _context.SaveChanges();

            return new SalesStats(
                statsOrders,
                generatedOrderItems,
                statsPayments,
                statsTickets,
                refundCount,
                exchangeCount,
                statsLocks,
                statsReservations);
        }

        /// <summary>为 REFUNDED/PART_REFUND 订单生成退款申请与明细</summary>
        private int CreateRefunds(List<OrderSeedRow> rows, DateTime now)
        {
            var policies = _context.Set<RefundPolicy>().ToList();
            var refunds = new List<RefundRequest>();
            var refundItems = new List<RefundItem>();
            int count = 0;

            foreach (var row in rows.Where(r => r.Kind is "REFUNDED" or "PART_REFUND"))
            {
                var order = row.Order;
                var items = order.Items.ToList();
                if (items.Count == 0)
                {
                    continue;
                }

                var selected = row.Kind == "REFUNDED"
                    ? items
                    : items.OrderBy(_ => Guid.NewGuid()).Take(Math.Max(1, items.Count / 2)).ToList();
                var refundAmount = selected.Sum(i => i.UnitPrice);

                // 选择一条策略：默认全局策略；带手续费时保证实际退款金额 > 0
                var policy = policies
                    .Where(p => p.Status == 1 && (p.ShowId is null || p.ShowId == row.Session.ShowId))
                    .OrderBy(p => p.Priority)
                    .FirstOrDefault();
                if (policy is null)
                {
                    continue;
                }

                decimal fee = policy.ServiceFee;
                decimal rate = policy.RefundRate;
                decimal actual = decimal.Round(refundAmount * rate - fee, 2, MidpointRounding.AwayFromZero);
                if (actual <= 0m)
                {
                    // 兜底：避免 CHECK(ACTUAL_REFUND IS NULL OR ACTUAL_REFUND > 0) 触发
                    rate = 1.00m;
                    fee = 0m;
                    actual = refundAmount;
                }

                var refund = new RefundRequest
                {
                    RefundNo = CreateBusinessNo("RFD", now),
                    OrderId = order.OrderId,
                    UserId = order.UserId,
                    RefundType = row.Kind == "REFUNDED" ? "FULL" : "PART",
                    RefundReason = _faker.PickRandom(new[]
                    {
                        "行程冲突无法到场",
                        "临时有事无法观看",
                        "买错场次，申请退票",
                        "身体原因无法参加"
                    }),
                    RefundAmount = refundAmount,
                    ActualRefund = actual,
                    FeeRate = rate,
                    AppliedPolicyId = policy.PolicyId,
                    AppliedServiceFee = fee,
                    ApproveStatus = "APPROVED",
                    ReviewBy = AdminUserName,
                    ReviewTime = now.AddMinutes(-_random.Next(1, 60)),
                    ReviewRemark = "审核通过，同意退款",
                    RefundStatus = "COMPLETED",
                    CompleteTime = now.AddMinutes(-_random.Next(1, 30)),
                    CreateBy = order.CreateBy,
                    UpdateBy = order.UpdateBy
                };
                _context.Set<RefundRequest>().Add(refund);
                refunds.Add(refund);
                _context.SaveChanges();

                var selectedIds = selected.Select(i => i.OrderItemId).ToHashSet();
                foreach (var item in selected)
                {
                    item.ItemStatus = "REFUNDED";
                    item.UpdateBy = order.UpdateBy;
                    refundItems.Add(new RefundItem
                    {
                        RefundId = refund.RefundId,
                        OrderItemId = item.OrderItemId,
                        RefundBaseAmount = item.UnitPrice,
                        CreateBy = order.CreateBy,
                        UpdateBy = order.UpdateBy
                    });

                    // 已出票的电子票同步为 REFUNDED
                    var ticket = _context.Set<ETicket>()
                        .FirstOrDefault(t => t.OrderItemId == item.OrderItemId);
                    if (ticket is not null)
                    {
                        ticket.TicketStatus = "REFUNDED";
                        ticket.UpdateBy = order.UpdateBy;
                    }
                }

                order.OrderStatus = row.Kind == "REFUNDED" ? "REFUNDED" : "PART_REFUND";
                order.UpdateBy = order.UpdateBy;

                // 释放被退座位的正式占座；同步支付流水的累计退款金额
                var reservations = _context.Set<SeatReservation>()
                    .Where(r => r.OrderItemId.HasValue && selectedIds.Contains(r.OrderItemId.Value))
                    .ToList();
                foreach (var reservation in reservations)
                {
                    reservation.ReservationStatus = "CANCELLED";
                    reservation.CancelTime ??= reservation.ReserveTime
                        .AddMinutes(_random.Next(1, 60));
                    reservation.UpdateBy = order.UpdateBy;
                }

                var payment = _context.Set<Payment>()
                    .FirstOrDefault(p => p.OrderId == order.OrderId);
                if (payment is not null)
                {
                    payment.RefundAmount = actual;
                    payment.UpdateBy = order.UpdateBy;
                }

                _context.Set<RefundItem>().AddRange(
                    refundItems.Where(r => r.RefundId == refund.RefundId));
                _context.SaveChanges();
                count++;
            }

            return count;
        }

        /// <summary>
        /// 生成少量“已完成的改签”：原订单部分/全部电子票改签至同演出的另一场次，
        /// 生成 EXCHANGE 子订单、支付补差、新的电子票与原票作废。
        /// </summary>
        private int CreateExchanges(
            List<OrderSeedRow> rows,
            IReadOnlyCollection<Show> shows,
            IReadOnlyCollection<ShowSession> sessions,
            IReadOnlyCollection<SeatSection> sections,
            IReadOnlyCollection<Seat> seats,
            IReadOnlyDictionary<long, HashSet<long>> usedSeatBySession,
            DateTime now)
        {
            var showMap = shows.ToDictionary(s => s.ShowId, s => s);
            var sessionList = sessions.ToList();
            var seatsBySection = seats.GroupBy(s => s.SeatSectionId)
                .ToDictionary(g => g.Key, g => g.ToList());
            var strategyList = _context.Set<PriceStrategy>().ToList();
            var exchangePolicies = _context.Set<ExchangePolicy>().ToList();
            if (exchangePolicies.Count == 0)
            {
                return 0;
            }

            var candidates = rows
                .Where(r => r.Kind == "ISSUED" &&
                            showMap.ContainsKey(r.Session.ShowId))
                .ToList();
            int exchangeTarget = (int)Math.Floor(candidates.Count * _exchangeOrderRatio);
            if (exchangeTarget <= 0)
            {
                return 0;
            }

            var usedBySession = usedSeatBySession
                .ToDictionary(kv => kv.Key, kv => new HashSet<long>(kv.Value));
            int created = 0;

            foreach (var row in candidates.OrderBy(_ => Guid.NewGuid()).Take(exchangeTarget))
            {
                var original = row.Order;
                var originalSession = row.Session;
                if (original.Items.Count == 0 || !showMap.TryGetValue(originalSession.ShowId, out var show))
                {
                    continue;
                }

                // 目标场次：同演出、非原场次、未开演/开售中的场次
                var targetCandidates = sessionList
                    .Where(s => s.ShowId == show.ShowId &&
                                s.SessionId != originalSession.SessionId &&
                                s.SessionStatus is "ONSALE" or "PRESALE")
                    .ToList();
                if (targetCandidates.Count == 0)
                {
                    continue;
                }

                var targetSession = targetCandidates[_random.Next(targetCandidates.Count)];
                var targetStrategies = strategyList
                    .Where(p => p.SessionId == targetSession.SessionId && p.Status == "ENABLED")
                    .ToList();
                if (targetStrategies.Count == 0)
                {
                    continue;
                }

                var targetUsed = usedBySession.GetValueOrDefault(targetSession.SessionId)
                    ?? new HashSet<long>();
                var targetUsable = sections
                    .Where(s => s.SeatMapId == targetSession.SeatMapId && s.IsSellable)
                    .SelectMany(s => seatsBySection.TryGetValue(s.SeatSectionId, out var list) ? list : new List<Seat>())
                    .Where(s => s.SeatStatus == "ENABLED" && s.IsSellable && !targetUsed.Contains(s.SeatId))
                    .OrderBy(_ => Guid.NewGuid())
                    .Take(original.Items.Count)
                    .ToList();
                if (targetUsable.Count != original.Items.Count)
                {
                    continue;
                }

                var policy = exchangePolicies
                    .Where(p => p.Status == 1 && (p.ShowId is null || p.ShowId == show.ShowId))
                    .OrderBy(p => p.Priority)
                    .FirstOrDefault();
                if (policy is null)
                {
                    continue;
                }

                var childOrder = new Order
                {
                    OrderNo = CreateBusinessNo("EXO", now),
                    UserId = original.UserId,
                    SessionId = targetSession.SessionId,
                    OrderType = "EXCHANGE",
                    ParentOrderId = original.OrderId,
                    TotalAmount = 0m,
                    DiscountAmount = 0m,
                    TicketCount = original.Items.Count,
                    OrderStatus = "PENDING_PAY",
                    ExpireTime = now.AddMinutes(15),
                    PayTime = null,
                    IssueTime = null,
                    CancelTime = null,
                    Source = original.Source,
                    IpAddress = GenerateIp(),
                    Remark = "改签生成子订单",
                    IdempotencyKey = null,
                    IdempotencyRequestHash = null,
                    CreateBy = original.CreateBy,
                    UpdateBy = original.UpdateBy
                };

                var exchange = new ExchangeRequest
                {
                    ExchangeNo = CreateBusinessNo("EXC", now),
                    OrderId = original.OrderId,
                    UserId = original.UserId,
                    OrigSessionId = originalSession.SessionId,
                    TargetSessionId = targetSession.SessionId,
                    ExchangeReason = _faker.PickRandom(new[]
                    {
                        "原场次时间冲突，申请改签",
                        "想和朋友同一场次，申请改签",
                        "临时安排有变，改签到其他场次"
                    }),
                    ExchangeFee = policy.ExchangeFee,
                    PriceDiff = 0m,
                    AppliedPolicyId = policy.PolicyId,
                    ApproveStatus = "APPROVED",
                    ReviewBy = AdminUserName,
                    ReviewTime = now.AddMinutes(-_random.Next(5, 120)),
                    ReviewRemark = "审核通过",
                    ExchangeStatus = "COMPLETED",
                    CompleteTime = now.AddMinutes(-_random.Next(1, 60)),
                    CreateBy = original.CreateBy,
                    UpdateBy = original.UpdateBy
                };

                _context.Set<Order>().Add(childOrder);
                _context.Set<ExchangeRequest>().Add(exchange);
                _context.SaveChanges();

                var exchangeItems = new List<ExchangeItem>();
                var originalItems = original.Items.ToList();
                for (int i = 0; i < originalItems.Count; i++)
                {
                    var originalItem = originalItems[i];
                    var targetSeat = targetUsable[i];
                    var strategy = targetStrategies
                        .FirstOrDefault(p => p.SeatSectionId == targetSeat.SeatSectionId) ??
                        targetStrategies.OrderBy(_ => Guid.NewGuid()).First();
                    var newItem = new OrderItem
                    {
                        OrderId = childOrder.OrderId,
                        SeatId = targetSeat.SeatId,
                        PriceStrategyId = strategy.PriceStrategyId,
                        RealNameId = originalItem.RealNameId,
                        UnitPrice = strategy.Price,
                        ItemStatus = "NORMAL",
                        CreateBy = original.CreateBy,
                        UpdateBy = original.UpdateBy
                    };
                    _context.Set<OrderItem>().Add(newItem);
                    _context.SaveChanges();

                    decimal diff = Math.Max(0m, strategy.Price - originalItem.UnitPrice);
                    exchange.PriceDiff += diff;
                    exchangeItems.Add(new ExchangeItem
                    {
                        ExchangeId = exchange.ExchangeId,
                        OrderItemId = originalItem.OrderItemId,
                        NewOrderItemId = newItem.OrderItemId,
                        CreateBy = original.CreateBy,
                        UpdateBy = original.UpdateBy
                    });

                    // 原票作废
                    originalItem.ItemStatus = "EXCHANGED";
                    originalItem.UpdateBy = original.CreateBy;
                    var originalTicket = _context.Set<ETicket>()
                        .FirstOrDefault(t => t.OrderItemId == originalItem.OrderItemId);
                    if (originalTicket is not null)
                    {
                        originalTicket.TicketStatus = "EXCHANGED";
                        originalTicket.UpdateBy = original.CreateBy;
                    }

                    var originalReservation = _context.Set<SeatReservation>()
                        .FirstOrDefault(r => r.OrderItemId == originalItem.OrderItemId);
                    if (originalReservation is not null)
                    {
                        originalReservation.ReservationStatus = "CANCELLED";
                        originalReservation.CancelTime ??= originalReservation.ReserveTime
                            .AddMinutes(_random.Next(1, 60));
                        originalReservation.UpdateBy = original.CreateBy;
                    }

                    // 新票/锁座/占座
                    var newLockTime = now.AddMinutes(-_random.Next(5, 240));
                    var newLock = new SeatLock
                    {
                        SessionId = targetSession.SessionId,
                        SeatId = targetSeat.SeatId,
                        UserId = original.UserId,
                        LockToken = Guid.NewGuid().ToString("N"),
                        LockStatus = "CONVERTED",
                        LockTime = newLockTime,
                        ExpireTime = newLockTime.AddMinutes(10),
                        ReleaseTime = newLockTime.AddMinutes(_random.Next(1, 9)),
                        CreateBy = original.CreateBy,
                        UpdateBy = original.UpdateBy
                    };
                    _context.Set<SeatLock>().Add(newLock);
                    _context.SaveChanges();

                    var newReserveTime = now.AddMinutes(-_random.Next(5, 240));
                    _context.Set<SeatReservation>().Add(new SeatReservation
                    {
                        SessionId = targetSession.SessionId,
                        SeatId = targetSeat.SeatId,
                        OrderItemId = newItem.OrderItemId,
                        SeatLockId = newLock.SeatLockId,
                        ReservationType = "ORDER",
                        ReservationStatus = "ACTIVE",
                        ReserveTime = newReserveTime,
                        HoldReason = "EXCHANGE",
                        Remark = "改签新座位",
                        CreateBy = original.CreateBy,
                        UpdateBy = original.UpdateBy
                    });

                    _context.Set<ETicket>().Add(new ETicket
                    {
                        ETicketNo = CreateBusinessNo("TKT", now),
                        OrderItemId = newItem.OrderItemId,
                        UserId = original.UserId,
                        QrCode = $"SHOWTIME|{childOrder.OrderId}|{newItem.OrderItemId}|{Guid.NewGuid():N}",
                        AntiFakeCode = Guid.NewGuid().ToString("N")[..20].ToUpperInvariant(),
                        TicketStatus = "UNUSED",
                        CreateBy = original.CreateBy,
                        UpdateBy = original.UpdateBy
                    });

                    targetUsed.Add(targetSeat.SeatId);
                }

                childOrder.TotalAmount = exchange.ExchangeFee + exchange.PriceDiff;
                childOrder.OrderStatus = "ISSUED";
                childOrder.PayTime = now.AddMinutes(-_random.Next(5, 120));
                childOrder.IssueTime = childOrder.PayTime;

                _context.Set<Payment>().Add(new Payment
                {
                    PaymentNo = CreateBusinessNo("EXP", now),
                    OrderId = childOrder.OrderId,
                    UserId = original.UserId,
                    PayAmount = childOrder.TotalAmount,
                    PayChannel = "ALIPAY",
                    PayStatus = "SUCCESS",
                    TradeNo = CreateBusinessNo("MOCK", now),
                    CallbackData = $"{{\"exchange\":true,\"result\":\"SUCCESS\",\"orderId\":{childOrder.OrderId}}}",
                    CallbackTime = childOrder.PayTime,
                    PayTime = childOrder.PayTime,
                    RefundAmount = 0m,
                    CreateBy = original.CreateBy,
                    UpdateBy = original.UpdateBy
                });

                _context.Set<ExchangeItem>().AddRange(exchangeItems);
                _context.SaveChanges();
                created++;
            }

            return created;
        }

        private List<SysUser> LoadBuyers()
        {
            var buyers = _context.Set<SysUser>()
                .Include(u => u.RealNames)
                .Where(u => u.Status == 1 &&
                            u.UserRoles.Any(r => r.Role.RoleCode == "USER"))
                .OrderBy(u => u.UserId)
                .ToList();
            if (buyers.Count == 0)
            {
                buyers = _context.Set<SysUser>()
                    .Include(u => u.RealNames)
                    .Where(u => u.Status == 1)
                    .OrderBy(u => u.UserId)
                    .ToList();
            }
            return buyers;
        }

        private long? PickRealNameId(SysUser user)
        {
            var verified = user.RealNames
                .Where(r => r.IsVerified)
                .OrderByDescending(r => r.IsDefault)
                .ThenBy(r => r.RealNameId)
                .ToList();
            return verified.Count > 0 ? verified[0].RealNameId : null;
        }

        private PriceStrategy PickPriceStrategy(IReadOnlyList<PriceStrategy> list)
        {
            return list
                .OrderByDescending(p => p.PriceType == "STANDARD")
                .ThenBy(p => p.Price)
                .First();
        }

        private string PickOrderKind(string sessionStatus, int itemCount)
        {
            var roll = _random.NextDouble();
            if (sessionStatus is "ENDED" or "SOLD_OUT")
            {
                if (roll < 0.72) return "ISSUED";
                if (roll < 0.81 && itemCount >= 2) return "PART_REFUND";
                if (roll < 0.90) return "REFUNDED";
                if (roll < 0.97) return "CANCELLED";
                return "PAID";
            }

            // ONSALE
            if (roll < 0.62) return "ISSUED";
            if (roll < 0.70) return "PENDING_PAY";
            if (roll < 0.80) return "CANCELLED";
            if (roll < 0.84) return "PAID";
            if (roll < 0.93 && itemCount >= 2) return "PART_REFUND";
            return "REFUNDED";
        }

        /// <summary>业务编号：前缀 + 时间戳 + 随机片段，全大写并截断到 28 位（与后端 CreateBusinessNumber 一致）</summary>
        private static string CreateBusinessNo(string prefix, DateTime now) =>
            (prefix + now.ToString("yyyyMMddHHmmssfff") + Guid.NewGuid().ToString("N"))[..28].ToUpperInvariant();

        private DateTime? PickCheckTime(DateTime start, DateTime end)
        {
            if (end <= start)
            {
                return null;
            }
            var span = end - start;
            var minutes = _random.Next(0, Math.Max(1, (int)span.TotalMinutes));
            var candidate = start.AddMinutes(minutes);
            return candidate <= DateTime.UtcNow ? candidate : DateTime.UtcNow.AddMinutes(-_random.Next(1, 20));
        }
    }
}
