using ShowtimeBackend.Entities.ShowSession;
using ShowtimeBackend.Services.ShowSession;

namespace ShowtimeBackend.Tests.ShowSessionTest;

public sealed class PricingTierSelectorTests
{
    private static readonly DateTime Now = new(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc);

    private static PriceStrategy Tier(
        long id,
        long sectionId,
        string priceType,
        decimal price,
        DateTime? saleStart = null,
        DateTime? saleEnd = null,
        int priority = 0,
        string status = "ENABLED") => new()
        {
            PriceStrategyId = id,
            SessionId = 1,
            SeatSectionId = sectionId,
            StrategyName = priceType,
            PriceType = priceType,
            Price = price,
            SaleStartTime = saleStart ?? default,
            SaleEndTime = saleEnd ?? default,
            Priority = priority,
            Status = status,
        };

    [Fact]
    public void SelectEffective_UnsetWindow_IsAlwaysActive()
    {
        var strategy = Tier(1, 10, "STANDARD", 100m);

        Assert.True(PricingTierSelector.IsActive(strategy, Now.AddDays(-999)));
        Assert.True(PricingTierSelector.IsActive(strategy, Now.AddDays(999)));
        Assert.Equal(strategy, PricingTierSelector.SelectEffective([strategy], 10, Now));
    }

    [Fact]
    public void SelectEffective_FiltersByWindow()
    {
        var early = Tier(1, 10, "EARLY_BIRD", 100m, Now.AddDays(-3), Now.AddHours(1));
        var standard = Tier(2, 10, "STANDARD", 180m, Now.AddHours(2), Now.AddDays(3));

        // 当前在早鸟窗口内 → 早鸟生效
        Assert.Equal("EARLY_BIRD", PricingTierSelector.SelectEffective([early, standard], 10, Now)?.PriceType);

        // 推进到标准窗口后 → 标准生效，早鸟过期不再返回
        var later = Now.AddDays(1);
        Assert.Equal("STANDARD", PricingTierSelector.SelectEffective([early, standard], 10, later)?.PriceType);
    }

    [Fact]
    public void SelectEffective_PriorityWinsWhenWindowsOverlap()
    {
        var low = Tier(1, 10, "EARLY_BIRD", 100m, Now.AddDays(-1), Now.AddDays(1), priority: 1);
        var high = Tier(2, 10, "STANDARD", 180m, Now.AddDays(-1), Now.AddDays(1), priority: 10);

        Assert.Equal(high, PricingTierSelector.SelectEffective([low, high], 10, Now));
    }

    [Fact]
    public void SelectEffective_TieBreaksByPriceTypeOrder()
    {
        // 同优先级重叠：早鸟优先于标准（保证“更优惠档”优先）
        var standard = Tier(1, 10, "STANDARD", 180m, Now.AddDays(-1), Now.AddDays(1), priority: 5);
        var early = Tier(2, 10, "EARLY_BIRD", 100m, Now.AddDays(-1), Now.AddDays(1), priority: 5);

        Assert.Equal(early, PricingTierSelector.SelectEffective([standard, early], 10, Now));
    }

    [Fact]
    public void SelectEffective_ExcludesDisabledAndOtherSections()
    {
        var enabled = Tier(1, 10, "STANDARD", 100m, Now.AddDays(-1), Now.AddDays(1));
        var disabled = Tier(2, 10, "EARLY_BIRD", 50m, Now.AddDays(-1), Now.AddDays(1), status: "DISABLED");
        var otherSection = Tier(3, 99, "VIP", 300m, Now.AddDays(-1), Now.AddDays(1));

        var selected = PricingTierSelector.SelectEffective([enabled, disabled, otherSection], 10, Now);

        Assert.Equal(enabled, selected);
    }

    [Fact]
    public void SelectEffective_NoActiveTier_ReturnsNull()
    {
        var future = Tier(1, 10, "STANDARD", 100m, Now.AddDays(2), Now.AddDays(3));
        var ended = Tier(2, 10, "EARLY_BIRD", 100m, Now.AddDays(-3), Now.AddDays(-1));

        Assert.Null(PricingTierSelector.SelectEffective([future, ended], 10, Now));
    }

    [Fact]
    public void SelectEffective_TieByStrategyId_IsStable()
    {
        var a = Tier(5, 10, "STANDARD", 100m, Now.AddDays(-1), Now.AddDays(1), priority: 3);
        var b = Tier(3, 10, "STANDARD", 100m, Now.AddDays(-1), Now.AddDays(1), priority: 3);

        Assert.Equal(b, PricingTierSelector.SelectEffective([a, b], 10, Now));
    }
}
