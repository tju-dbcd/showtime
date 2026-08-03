using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShowtimeBackend.Entities.SeatZone;

namespace ShowtimeBackend.Data.Configurations.SeatZone;

public class SeatRuleConfiguration : IEntityTypeConfiguration<SeatRule>
{
    public void Configure(EntityTypeBuilder<SeatRule> builder)
    {
        builder.ToTable("SEAT_RULE", table =>
        {
            table.HasCheckConstraint("CK_SEAT_RULE_TYPE", "RULE_TYPE IN ('CONTINUOUS', 'NO_SINGLE_LEFT', 'LIMIT_COUNT', 'SECTION_LIMIT')");
            table.HasCheckConstraint("CK_SEAT_RULE_CROSS_ROW", "ALLOW_CROSS_ROW IN ('Y', 'N')");
            table.HasCheckConstraint("CK_SEAT_RULE_CROSS_SECTION", "ALLOW_CROSS_SECTION IN ('Y', 'N')");
            table.HasCheckConstraint("CK_SEAT_RULE_STATUS", "RULE_STATUS IN ('ENABLED', 'DISABLED')");
            table.HasCheckConstraint("CK_SEAT_RULE_COUNT", "MIN_SEAT_COUNT <= MAX_SEAT_COUNT");
            table.HasCheckConstraint("CK_SEAT_RULE_PRIORITY", "PRIORITY >= 0");
        });
        builder.HasKey(entity => entity.SeatRuleId).HasName("PK_SEAT_RULE");
        builder.Property(entity => entity.SeatRuleId).HasColumnName("SEAT_RULE_ID").HasColumnType("NUMBER(19)").ValueGeneratedOnAdd();
        builder.Property(entity => entity.RuleCode).HasColumnName("RULE_CODE").HasColumnType("VARCHAR2(30 CHAR)").HasMaxLength(30).IsUnicode(false).IsRequired();
        builder.Property(entity => entity.RuleName).HasColumnName("RULE_NAME").HasColumnType("VARCHAR2(100 CHAR)").HasMaxLength(100).IsUnicode(false).IsRequired();
        builder.Property(entity => entity.RuleType).HasColumnName("RULE_TYPE").HasColumnType("VARCHAR2(30 CHAR)").HasMaxLength(30).IsUnicode(false).IsRequired();
        builder.Property(entity => entity.MinSeatCount).HasColumnName("MIN_SEAT_COUNT").HasColumnType("NUMBER(3)").HasDefaultValue(1).IsRequired();
        builder.Property(entity => entity.MaxSeatCount).HasColumnName("MAX_SEAT_COUNT").HasColumnType("NUMBER(3)").HasDefaultValue(10).IsRequired();
        builder.Property(entity => entity.AllowCrossRow).HasColumnName("ALLOW_CROSS_ROW").HasColumnType("CHAR(1)").HasConversion(value => value ? "Y" : "N", value => value == "Y").HasDefaultValue(false).IsRequired();
        builder.Property(entity => entity.AllowCrossSection).HasColumnName("ALLOW_CROSS_SECTION").HasColumnType("CHAR(1)").HasConversion(value => value ? "Y" : "N", value => value == "Y").HasDefaultValue(false).IsRequired();
        builder.Property(entity => entity.Priority).HasColumnName("PRIORITY").HasColumnType("NUMBER(5)").HasDefaultValue(100).IsRequired();
        builder.Property(entity => entity.RuleStatus).HasColumnName("RULE_STATUS").HasColumnType("VARCHAR2(20 CHAR)").HasMaxLength(20).IsUnicode(false).HasDefaultValue("ENABLED").IsRequired();
        builder.Property(entity => entity.Remark).HasColumnName("REMARK").HasColumnType("VARCHAR2(255 CHAR)").HasMaxLength(255).IsUnicode(false);
        builder.ConfigureSeatZoneAuditableEntity();
        builder.HasIndex(entity => entity.RuleCode).IsUnique().HasDatabaseName("UK_SEAT_RULE_CODE");
    }
}
