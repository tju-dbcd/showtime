using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShowtimeBackend.Entities.SeatZone;

namespace ShowtimeBackend.Data.Configurations.SeatZone;

public class SeatConfiguration : IEntityTypeConfiguration<Seat>
{
    public void Configure(EntityTypeBuilder<Seat> builder)
    {
        builder.ToTable("SEAT", table =>
        {
            table.HasCheckConstraint("CK_SEAT_TYPE", "SEAT_TYPE IN ('NORMAL', 'COUPLE', 'ACCESSIBLE', 'COMPANION')");
            table.HasCheckConstraint("CK_SEAT_STATUS", "SEAT_STATUS IN ('ENABLED', 'DISABLED', 'MAINTENANCE')");
            table.HasCheckConstraint("CK_SEAT_AISLE_SIDE", "IS_AISLE_SIDE IN ('Y', 'N')");
            table.HasCheckConstraint("CK_SEAT_SELLABLE", "IS_SELLABLE IN ('Y', 'N')");
            table.HasCheckConstraint("CK_SEAT_INDEX", "ROW_INDEX >= 0 AND COL_INDEX >= 0");
        });
        builder.HasKey(entity => entity.SeatId).HasName("PK_SEAT");
        builder.Property(entity => entity.SeatId).HasColumnName("SEAT_ID").HasColumnType("NUMBER(19)").ValueGeneratedOnAdd();
        builder.Property(entity => entity.SeatSectionId).HasColumnName("SEAT_SECTION_ID").HasColumnType("NUMBER(19)").IsRequired();
        builder.Property(entity => entity.RowCode).HasColumnName("ROW_CODE").HasColumnType("VARCHAR2(20 CHAR)").HasMaxLength(20).IsUnicode(false).IsRequired();
        builder.Property(entity => entity.SeatNo).HasColumnName("SEAT_NO").HasColumnType("VARCHAR2(20 CHAR)").HasMaxLength(20).IsUnicode(false).IsRequired();
        builder.Property(entity => entity.RowIndex).HasColumnName("ROW_INDEX").HasColumnType("NUMBER(5)").IsRequired();
        builder.Property(entity => entity.ColIndex).HasColumnName("COL_INDEX").HasColumnType("NUMBER(5)").IsRequired();
        builder.Property(entity => entity.XCoord).HasColumnName("X_COORD").HasColumnType("NUMBER(10,2)").HasDefaultValue(0m).IsRequired();
        builder.Property(entity => entity.YCoord).HasColumnName("Y_COORD").HasColumnType("NUMBER(10,2)").HasDefaultValue(0m).IsRequired();
        builder.Property(entity => entity.SeatType).HasColumnName("SEAT_TYPE").HasColumnType("VARCHAR2(20 CHAR)").HasMaxLength(20).IsUnicode(false).HasDefaultValue("NORMAL").IsRequired();
        builder.Property(entity => entity.SeatStatus).HasColumnName("SEAT_STATUS").HasColumnType("VARCHAR2(20 CHAR)").HasMaxLength(20).IsUnicode(false).HasDefaultValue("ENABLED").IsRequired();
        builder.Property(entity => entity.IsAisleSide).HasColumnName("IS_AISLE_SIDE").HasColumnType("CHAR(1)").HasConversion(value => value ? "Y" : "N", value => value == "Y").HasDefaultValue(false).IsRequired();
        builder.Property(entity => entity.IsSellable).HasColumnName("IS_SELLABLE").HasColumnType("CHAR(1)").HasConversion(value => value ? "Y" : "N", value => value == "Y").HasDefaultValue(true).IsRequired();
        builder.Property(entity => entity.Remark).HasColumnName("REMARK").HasColumnType("VARCHAR2(255 CHAR)").HasMaxLength(255).IsUnicode(false);
        builder.ConfigureSeatZoneAuditableEntity();
        builder.HasIndex(entity => new { entity.SeatSectionId, entity.RowCode, entity.SeatNo }).IsUnique().HasDatabaseName("UK_SEAT_ROW_NO");
        builder.HasIndex(entity => new { entity.SeatSectionId, entity.RowIndex, entity.ColIndex }).IsUnique().HasDatabaseName("UK_SEAT_POSITION");
        builder.HasIndex(entity => entity.SeatSectionId).HasDatabaseName("IDX_SEAT_SECTION_ID");
        builder.HasIndex(entity => entity.SeatType).HasDatabaseName("IDX_SEAT_TYPE");
        builder.HasIndex(entity => entity.SeatStatus).HasDatabaseName("IDX_SEAT_STATUS");
        builder.HasOne(entity => entity.SeatSection).WithMany(entity => entity.Seats).HasForeignKey(entity => entity.SeatSectionId).OnDelete(DeleteBehavior.NoAction).HasConstraintName("FK_SEAT_SECTION");
    }
}
