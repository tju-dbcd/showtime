using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShowtimeBackend.Entities.SeatZone;

namespace ShowtimeBackend.Data.Configurations.SeatZone;

public class SeatLockConfiguration : IEntityTypeConfiguration<SeatLock>
{
    public void Configure(EntityTypeBuilder<SeatLock> builder)
    {
        builder.ToTable("SEAT_LOCK", table =>
        {
            table.HasCheckConstraint("CK_SEAT_LOCK_STATUS", "LOCK_STATUS IN ('ACTIVE', 'RELEASED', 'EXPIRED', 'CONVERTED')");
            table.HasCheckConstraint("CK_SEAT_LOCK_TIME", "EXPIRE_TIME > LOCK_TIME");
            table.HasCheckConstraint("CK_SEAT_LOCK_RELEASE_TIME", "RELEASE_TIME IS NULL OR RELEASE_TIME >= LOCK_TIME");
        });
        builder.HasKey(entity => entity.SeatLockId).HasName("PK_SEAT_LOCK");
        builder.Property(entity => entity.SeatLockId).HasColumnName("SEAT_LOCK_ID").HasColumnType("NUMBER(19)").ValueGeneratedOnAdd();
        // SHOW_SESSION and SYS_USER belong to other modules, so only their foreign-key IDs are mapped here.
        builder.Property(entity => entity.SessionId).HasColumnName("SESSION_ID").HasColumnType("NUMBER(19)").IsRequired();
        builder.Property(entity => entity.SeatId).HasColumnName("SEAT_ID").HasColumnType("NUMBER(19)").IsRequired();
        builder.Property(entity => entity.UserId).HasColumnName("USER_ID").HasColumnType("NUMBER(19)").IsRequired();
        builder.Property(entity => entity.LockToken).HasColumnName("LOCK_TOKEN").HasColumnType("VARCHAR2(64 CHAR)").HasMaxLength(64).IsUnicode(false).IsRequired();
        builder.Property(entity => entity.LockStatus).HasColumnName("LOCK_STATUS").HasColumnType("VARCHAR2(20 CHAR)").HasMaxLength(20).IsUnicode(false).HasDefaultValue("ACTIVE").IsRequired();
        builder.Property(entity => entity.LockTime).HasColumnName("LOCK_TIME").HasColumnType("TIMESTAMP(6)").HasDefaultValueSql("CURRENT_TIMESTAMP").ValueGeneratedOnAdd().IsRequired();
        builder.Property(entity => entity.ExpireTime).HasColumnName("EXPIRE_TIME").HasColumnType("TIMESTAMP(6)").IsRequired();
        builder.Property(entity => entity.ReleaseTime).HasColumnName("RELEASE_TIME").HasColumnType("TIMESTAMP(6)");
        builder.Property(entity => entity.Remark).HasColumnName("REMARK").HasColumnType("VARCHAR2(255 CHAR)").HasMaxLength(255).IsUnicode(false);
        builder.ConfigureSeatZoneAuditableEntity();
        builder.HasIndex(entity => entity.LockToken).IsUnique().HasDatabaseName("UK_SEAT_LOCK_TOKEN");
        builder.HasIndex(entity => entity.SessionId).HasDatabaseName("IDX_SEAT_LOCK_SESSION");
        builder.HasIndex(entity => entity.UserId).HasDatabaseName("IDX_SEAT_LOCK_USER");
        builder.HasIndex(entity => entity.ExpireTime).HasDatabaseName("IDX_SEAT_LOCK_EXPIRE");
        builder.HasOne<Seat>().WithMany().HasForeignKey(entity => entity.SeatId).OnDelete(DeleteBehavior.NoAction).HasConstraintName("FK_SEAT_LOCK_SEAT");
        // UK_SEAT_LOCK_ACTIVE is an existing Oracle CASE expression index and is intentionally not modeled by EF.
    }
}
