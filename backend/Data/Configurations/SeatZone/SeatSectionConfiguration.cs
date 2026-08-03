using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShowtimeBackend.Entities.SeatZone;

namespace ShowtimeBackend.Data.Configurations.SeatZone;

public class SeatSectionConfiguration : IEntityTypeConfiguration<SeatSection>
{
    public void Configure(EntityTypeBuilder<SeatSection> builder)
    {
        builder.ToTable("SEAT_SECTION", table =>
        {
            table.HasCheckConstraint("CK_SEAT_SECTION_TYPE", "SECTION_TYPE IN ('NORMAL', 'VIP', 'ACCESSIBLE', 'STANDING')");
            table.HasCheckConstraint("CK_SEAT_SECTION_SELLABLE", "IS_SELLABLE IN ('Y', 'N')");
            table.HasCheckConstraint("CK_SEAT_SECTION_DISPLAY_ORDER", "DISPLAY_ORDER >= 0");
        });
        builder.HasKey(entity => entity.SeatSectionId).HasName("PK_SEAT_SECTION");
        builder.Property(entity => entity.SeatSectionId).HasColumnName("SEAT_SECTION_ID").HasColumnType("NUMBER(19)").ValueGeneratedOnAdd();
        builder.Property(entity => entity.SeatMapId).HasColumnName("SEAT_MAP_ID").HasColumnType("NUMBER(19)").IsRequired();
        builder.Property(entity => entity.SectionCode).HasColumnName("SECTION_CODE").HasColumnType("VARCHAR2(30 CHAR)").HasMaxLength(30).IsUnicode(false).IsRequired();
        builder.Property(entity => entity.SectionName).HasColumnName("SECTION_NAME").HasColumnType("VARCHAR2(100 CHAR)").HasMaxLength(100).IsUnicode(false).IsRequired();
        builder.Property(entity => entity.SectionType).HasColumnName("SECTION_TYPE").HasColumnType("VARCHAR2(20 CHAR)").HasMaxLength(20).IsUnicode(false).HasDefaultValue("NORMAL").IsRequired();
        builder.Property(entity => entity.SectionColor).HasColumnName("SECTION_COLOR").HasColumnType("VARCHAR2(20 CHAR)").HasMaxLength(20).IsUnicode(false);
        builder.Property(entity => entity.FloorNo).HasColumnName("FLOOR_NO").HasColumnType("VARCHAR2(20 CHAR)").HasMaxLength(20).IsUnicode(false);
        builder.Property(entity => entity.IsSellable).HasColumnName("IS_SELLABLE").HasColumnType("CHAR(1)").HasConversion(value => value ? "Y" : "N", value => value == "Y").HasDefaultValue(true).IsRequired();
        builder.Property(entity => entity.DisplayOrder).HasColumnName("DISPLAY_ORDER").HasColumnType("NUMBER(5)").HasDefaultValue(0).IsRequired();
        builder.Property(entity => entity.Remark).HasColumnName("REMARK").HasColumnType("VARCHAR2(255 CHAR)").HasMaxLength(255).IsUnicode(false);
        builder.ConfigureSeatZoneAuditableEntity();
        builder.HasIndex(entity => new { entity.SeatMapId, entity.SectionCode }).IsUnique().HasDatabaseName("UK_SEAT_SECTION_MAP_CODE");
        builder.HasIndex(entity => entity.SeatMapId).HasDatabaseName("IDX_SEAT_SECTION_MAP");
        builder.HasIndex(entity => new { entity.SeatMapId, entity.DisplayOrder }).HasDatabaseName("IDX_SEAT_SECTION_ORDER");
        builder.HasIndex(entity => entity.IsSellable).HasDatabaseName("IDX_SEAT_SECTION_SELLABLE");
        builder.HasOne(entity => entity.SeatMap).WithMany(entity => entity.Sections).HasForeignKey(entity => entity.SeatMapId).OnDelete(DeleteBehavior.NoAction).HasConstraintName("FK_SEAT_SECTION_MAP");
    }
}
