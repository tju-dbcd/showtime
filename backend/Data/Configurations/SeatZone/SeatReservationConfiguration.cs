using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShowtimeBackend.Entities.SeatZone;

namespace ShowtimeBackend.Data.Configurations.SeatZone;

public class SeatReservationConfiguration : IEntityTypeConfiguration<SeatReservation>
{
    public void Configure(EntityTypeBuilder<SeatReservation> builder)
    {
        builder.ToTable("SEAT_RESERVATION", table =>
        {
            table.HasCheckConstraint("CK_SEAT_RESERVATION_TYPE", "RESERVATION_TYPE IN ('ORDER', 'SYSTEM', 'VIP')");
            table.HasCheckConstraint("CK_SEAT_RESERVATION_STATUS", "RESERVATION_STATUS IN ('ACTIVE', 'CANCELLED', 'RELEASED')");
            table.HasCheckConstraint("CK_SEAT_RESERVATION_CANCEL", "CANCEL_TIME IS NULL OR CANCEL_TIME >= RESERVE_TIME");
            table.HasCheckConstraint("CK_SEAT_RESERVATION_ORDER_ITEM", "(RESERVATION_TYPE = 'ORDER' AND ORDER_ITEM_ID IS NOT NULL) OR (RESERVATION_TYPE IN ('SYSTEM', 'VIP') AND ORDER_ITEM_ID IS NULL)");
        });
        builder.HasKey(entity => entity.SeatReservationId).HasName("PK_SEAT_RESERVATION");
        builder.Property(entity => entity.SeatReservationId).HasColumnName("SEAT_RESERVATION_ID").HasColumnType("NUMBER(19)").ValueGeneratedOnAdd();
        // SHOW_SESSION and ORDER_ITEM are mapped by other modules; keep their IDs without cross-module navigations.
        builder.Property(entity => entity.SessionId).HasColumnName("SESSION_ID").HasColumnType("NUMBER(19)").IsRequired();
        builder.Property(entity => entity.SeatId).HasColumnName("SEAT_ID").HasColumnType("NUMBER(19)").IsRequired();
        builder.Property(entity => entity.OrderItemId).HasColumnName("ORDER_ITEM_ID").HasColumnType("NUMBER(19)");
        builder.Property(entity => entity.SeatLockId).HasColumnName("SEAT_LOCK_ID").HasColumnType("NUMBER(19)");
        builder.Property(entity => entity.ReservationType).HasColumnName("RESERVATION_TYPE").HasColumnType("VARCHAR2(20 CHAR)").HasMaxLength(20).IsUnicode(false).HasDefaultValue("ORDER").IsRequired();
        builder.Property(entity => entity.ReservationStatus).HasColumnName("RESERVATION_STATUS").HasColumnType("VARCHAR2(20 CHAR)").HasMaxLength(20).IsUnicode(false).HasDefaultValue("ACTIVE").IsRequired();
        builder.Property(entity => entity.ReserveTime).HasColumnName("RESERVE_TIME").HasColumnType("TIMESTAMP(6)").HasDefaultValueSql("CURRENT_TIMESTAMP").ValueGeneratedOnAdd().IsRequired();
        builder.Property(entity => entity.CancelTime).HasColumnName("CANCEL_TIME").HasColumnType("TIMESTAMP(6)");
        builder.Property(entity => entity.HoldReason).HasColumnName("HOLD_REASON").HasColumnType("VARCHAR2(255 CHAR)").HasMaxLength(255).IsUnicode(false);
        builder.Property(entity => entity.Remark).HasColumnName("REMARK").HasColumnType("VARCHAR2(255 CHAR)").HasMaxLength(255).IsUnicode(false);
        builder.ConfigureSeatZoneAuditableEntity();
        builder.HasIndex(entity => entity.OrderItemId).IsUnique().HasDatabaseName("UK_SEAT_RESERVATION_ORDER_ITEM");
        builder.HasIndex(entity => entity.SeatLockId).IsUnique().HasDatabaseName("UK_SEAT_RESERVATION_LOCK");
        builder.HasIndex(entity => entity.SessionId).HasDatabaseName("IDX_SEAT_RESERVATION_SESSION");
        builder.HasOne<Seat>().WithMany().HasForeignKey(entity => entity.SeatId).OnDelete(DeleteBehavior.NoAction).HasConstraintName("FK_SEAT_RESERVATION_SEAT");
        builder.HasOne<SeatLock>().WithMany().HasForeignKey(entity => entity.SeatLockId).OnDelete(DeleteBehavior.NoAction).HasConstraintName("FK_SEAT_RESERVATION_LOCK");
        // UK_SEAT_RESERVATION_ACTIVE is an existing Oracle CASE expression index and is intentionally not modeled by EF.
    }
}
