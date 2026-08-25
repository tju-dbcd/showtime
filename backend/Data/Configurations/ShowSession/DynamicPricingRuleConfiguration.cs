using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShowtimeBackend.Entities.ShowSession;

namespace ShowtimeBackend.Data.Configurations.ShowSession;

public class DynamicPricingRuleConfiguration : IEntityTypeConfiguration<DynamicPricingRule>
{
    public void Configure(EntityTypeBuilder<DynamicPricingRule> builder)
    {
        builder.ToTable("DYNAMIC_PRICING_RULE");

        builder.HasKey(r => r.DynamicPricingRuleId);
        builder.Property(r => r.DynamicPricingRuleId).HasColumnName("RULE_ID");
        builder.Property(r => r.SessionId).HasColumnName("SESSION_ID").IsRequired();
        builder.Property(r => r.SeatSectionId).HasColumnName("SEAT_SECTION_ID");
        builder.Property(r => r.RuleName).HasColumnName("RULE_NAME").HasMaxLength(100).IsRequired();
        builder.Property(r => r.TriggerType).HasColumnName("TRIGGER_TYPE").HasMaxLength(30).IsRequired();
        builder.Property(r => r.AdjustmentType).HasColumnName("ADJUSTMENT_TYPE").HasMaxLength(20).IsRequired();
        builder.Property(r => r.AdjustmentValue).HasColumnName("ADJUSTMENT_VALUE").HasColumnType("NUMBER(10,2)").IsRequired();
        builder.Property(r => r.Priority).HasColumnName("PRIORITY").HasDefaultValue(0);
        builder.Property(r => r.Status).HasColumnName("STATUS").HasMaxLength(20).HasDefaultValue("ENABLED");

        builder.Property(r => r.CreateBy).HasColumnName("CREATE_BY").HasMaxLength(64).IsRequired();
        builder.Property(r => r.CreateTime).HasColumnName("CREATE_TIME").IsRequired();
        builder.Property(r => r.UpdateBy).HasColumnName("UPDATE_BY").HasMaxLength(64).IsRequired();
        builder.Property(r => r.UpdateTime).HasColumnName("UPDATE_TIME").IsRequired();

        builder.HasIndex(r => r.SessionId).HasDatabaseName("IX_DPR_SESSION_ID");
        builder.HasIndex(r => r.SeatSectionId).HasDatabaseName("IX_DPR_SECTION_ID");
    }
}
