using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShowtimeBackend.Entities.SeatZone;

namespace ShowtimeBackend.Data.Configurations.SeatZone;

public class SeatRuleScopeConfiguration : IEntityTypeConfiguration<SeatRuleScope>
{
    public void Configure(EntityTypeBuilder<SeatRuleScope> builder)
    {
        builder.ToTable("SEAT_RULE_SCOPE", table =>
        {
            table.HasCheckConstraint("CK_SEAT_RULE_SCOPE_TYPE", "SCOPE_TYPE IN ('MAP', 'SECTION')");
            table.HasCheckConstraint("CK_SEAT_RULE_SCOPE_STATUS", "SCOPE_STATUS IN ('ENABLED', 'DISABLED')");
            table.HasCheckConstraint("CK_SEAT_RULE_SCOPE_TARGET", "(SCOPE_TYPE = 'MAP' AND SEAT_MAP_ID IS NOT NULL AND SEAT_SECTION_ID IS NULL) OR (SCOPE_TYPE = 'SECTION' AND SEAT_SECTION_ID IS NOT NULL AND SEAT_MAP_ID IS NULL)");
        });
        builder.HasKey(entity => entity.RuleScopeId).HasName("PK_SEAT_RULE_SCOPE");
        builder.Property(entity => entity.RuleScopeId).HasColumnName("RULE_SCOPE_ID").HasColumnType("NUMBER(19)").ValueGeneratedOnAdd();
        builder.Property(entity => entity.SeatRuleId).HasColumnName("SEAT_RULE_ID").HasColumnType("NUMBER(19)").IsRequired();
        builder.Property(entity => entity.ScopeType).HasColumnName("SCOPE_TYPE").HasColumnType("VARCHAR2(20 CHAR)").HasMaxLength(20).IsUnicode(false).IsRequired();
        builder.Property(entity => entity.SeatMapId).HasColumnName("SEAT_MAP_ID").HasColumnType("NUMBER(19)");
        builder.Property(entity => entity.SeatSectionId).HasColumnName("SEAT_SECTION_ID").HasColumnType("NUMBER(19)");
        builder.Property(entity => entity.ScopeStatus).HasColumnName("SCOPE_STATUS").HasColumnType("VARCHAR2(20 CHAR)").HasMaxLength(20).IsUnicode(false).HasDefaultValue("ENABLED").IsRequired();
        builder.ConfigureSeatZoneAuditableEntity();
        builder.HasIndex(entity => entity.SeatRuleId).HasDatabaseName("IDX_SEAT_RULE_SCOPE_RULE");
        builder.HasOne(entity => entity.SeatRule).WithMany(entity => entity.Scopes).HasForeignKey(entity => entity.SeatRuleId).OnDelete(DeleteBehavior.NoAction).HasConstraintName("FK_SEAT_RULE_SCOPE_RULE");
        builder.HasOne(entity => entity.SeatMap).WithMany(entity => entity.RuleScopes).HasForeignKey(entity => entity.SeatMapId).OnDelete(DeleteBehavior.NoAction).HasConstraintName("FK_SEAT_RULE_SCOPE_MAP");
        builder.HasOne(entity => entity.SeatSection).WithMany(entity => entity.RuleScopes).HasForeignKey(entity => entity.SeatSectionId).OnDelete(DeleteBehavior.NoAction).HasConstraintName("FK_SEAT_RULE_SCOPE_SECTION");
        // UK_SEAT_RULE_SCOPE_MAP and UK_SEAT_RULE_SCOPE_SECTION are Oracle CASE expression indexes and are intentionally not modeled by EF.
    }
}
