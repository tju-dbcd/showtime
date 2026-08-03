using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShowtimeBackend.Entities.SeatZone;

namespace ShowtimeBackend.Data.Configurations.SeatZone;

public class SeatMapConfiguration : IEntityTypeConfiguration<SeatMap>
{
    public void Configure(EntityTypeBuilder<SeatMap> builder)
    {
        builder.ToTable("SEAT_MAP", table =>
        {
            table.HasCheckConstraint("CK_SEAT_MAP_DEFAULT", "IS_DEFAULT IN ('Y', 'N')");
            table.HasCheckConstraint("CK_SEAT_MAP_STATUS", "MAP_STATUS IN ('DRAFT', 'ENABLED', 'DISABLED')");
            table.HasCheckConstraint("CK_SEAT_MAP_SIZE", "(MAP_WIDTH IS NULL OR MAP_WIDTH > 0) AND (MAP_HEIGHT IS NULL OR MAP_HEIGHT > 0)");
        });
        builder.HasKey(entity => entity.SeatMapId).HasName("PK_SEAT_MAP");
        builder.Property(entity => entity.SeatMapId).HasColumnName("SEAT_MAP_ID").HasColumnType("NUMBER(19)").ValueGeneratedOnAdd();
        builder.Property(entity => entity.VenueId).HasColumnName("VENUE_ID").HasColumnType("NUMBER(19)").IsRequired();
        builder.Property(entity => entity.MapCode).HasColumnName("MAP_CODE").HasColumnType("VARCHAR2(50 CHAR)").HasMaxLength(50).IsUnicode(false).IsRequired();
        builder.Property(entity => entity.MapName).HasColumnName("MAP_NAME").HasColumnType("VARCHAR2(100 CHAR)").HasMaxLength(100).IsUnicode(false).IsRequired();
        builder.Property(entity => entity.MapVersion).HasColumnName("MAP_VERSION").HasColumnType("VARCHAR2(20 CHAR)").HasMaxLength(20).IsUnicode(false).HasDefaultValue("V1").IsRequired();
        builder.Property(entity => entity.IsDefault).HasColumnName("IS_DEFAULT").HasColumnType("CHAR(1)").HasConversion(value => value ? "Y" : "N", value => value == "Y").HasDefaultValue(false).IsRequired();
        builder.Property(entity => entity.MapWidth).HasColumnName("MAP_WIDTH").HasColumnType("NUMBER(10,2)");
        builder.Property(entity => entity.MapHeight).HasColumnName("MAP_HEIGHT").HasColumnType("NUMBER(10,2)");
        builder.Property(entity => entity.MapStatus).HasColumnName("MAP_STATUS").HasColumnType("VARCHAR2(20 CHAR)").HasMaxLength(20).IsUnicode(false).HasDefaultValue("DRAFT").IsRequired();
        builder.Property(entity => entity.Remark).HasColumnName("REMARK").HasColumnType("VARCHAR2(255 CHAR)").HasMaxLength(255).IsUnicode(false);
        builder.ConfigureSeatZoneAuditableEntity();
        builder.HasIndex(entity => new { entity.VenueId, entity.MapCode }).IsUnique().HasDatabaseName("UK_SEAT_MAP_VENUE_CODE");
        builder.HasIndex(entity => entity.VenueId).HasDatabaseName("IDX_SEAT_MAP_VENUE");
        // UK_SEAT_MAP_DEFAULT is an existing Oracle CASE expression index and is intentionally not modeled by EF.
    }
}
