// backend/Data/Configurations/ShowSession/DynamicPricingRuleConfiguration.cs

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShowtimeBackend.Entities.SeatZone;
using ShowtimeBackend.Entities.ShowSession;

namespace ShowtimeBackend.Data.Configurations.ShowSession;

public class DynamicPricingRuleConfiguration : IEntityTypeConfiguration<DynamicPricingRule>
{
    public void Configure(EntityTypeBuilder<DynamicPricingRule> builder)
    {
        builder.ToTable("DYNAMIC_PRICING_RULE", table =>
        {
            table.HasCheckConstraint("CK_DPR_TRIGGER_TYPE", "TRIGGER_TYPE IN ('TIME_WINDOW', 'INVENTORY_RATE')");
            table.HasCheckConstraint("CK_DPR_ADJUSTMENT_TYPE", "ADJUSTMENT_TYPE IN ('DISCOUNT_RATE', 'AMOUNT_OFF', 'FIXED_PRICE')");
            table.HasCheckConstraint("CK_DPR_STATUS", "STATUS IN ('ENABLED', 'DISABLED')");
        });

        builder.HasKey(r => r.DynamicPricingRuleId).HasName("PK_DYNAMIC_PRICING_RULE");
        builder.Property(r => r.DynamicPricingRuleId)
               .HasColumnName("RULE_ID")
               .HasColumnType("NUMBER(19,0)")
               .ValueGeneratedOnAdd();

        builder.Property(r => r.SessionId).HasColumnName("SESSION_ID").HasColumnType("NUMBER(19,0)").IsRequired();
        builder.Property(r => r.SeatSectionId).HasColumnName("SEAT_SECTION_ID").HasColumnType("NUMBER(19,0)");
        builder.Property(r => r.RuleName).HasColumnName("RULE_NAME").HasColumnType("VARCHAR2(100 CHAR)").IsRequired();
        builder.Property(r => r.TriggerType).HasColumnName("TRIGGER_TYPE").HasColumnType("VARCHAR2(30 CHAR)").IsRequired();

        builder.Property(r => r.StartOffsetMinutes).HasColumnName("START_OFFSET_MINUTES").HasColumnType("NUMBER(10,0)");
        builder.Property(r => r.EndOffsetMinutes).HasColumnName("END_OFFSET_MINUTES").HasColumnType("NUMBER(10,0)");

        builder.Property(r => r.AdjustmentType).HasColumnName("ADJUSTMENT_TYPE").HasColumnType("VARCHAR2(20 CHAR)").IsRequired();
        builder.Property(r => r.AdjustmentValue).HasColumnName("ADJUSTMENT_VALUE").HasColumnType("NUMBER(10,2)").IsRequired();
        builder.Property(r => r.Priority).HasColumnName("PRIORITY").HasColumnType("NUMBER(5,0)").HasDefaultValue(0).IsRequired();
        builder.Property(r => r.Status).HasColumnName("STATUS").HasColumnType("VARCHAR2(20 CHAR)").HasDefaultValueSql("'ENABLED'").IsRequired();

        // 场次外键关联
        builder.HasOne(r => r.ShowSession)
               .WithMany()
               .HasForeignKey(r => r.SessionId)
               .HasConstraintName("FK_DPR_SESSION")
               .OnDelete(DeleteBehavior.Cascade);

        // 看台外键关联
        builder.HasOne<SeatSection>()
               .WithMany()
               .HasForeignKey(r => r.SeatSectionId)
               .HasConstraintName("FK_DPR_SECTION")
               .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(r => r.SessionId).HasDatabaseName("IDX_DPR_SESSION");
        builder.HasIndex(r => r.SeatSectionId).HasDatabaseName("IDX_DPR_SECTION");

        // 统一注入审计列配置
        builder.ConfigureAuditableEntity();
    }
}
