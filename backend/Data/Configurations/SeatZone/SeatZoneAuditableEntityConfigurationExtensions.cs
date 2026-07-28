using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShowtimeBackend.Entities.Base;

namespace ShowtimeBackend.Data.Configurations.SeatZone;

internal static class SeatZoneAuditableEntityConfigurationExtensions
{
    public static void ConfigureSeatZoneAuditableEntity<TEntity>(this EntityTypeBuilder<TEntity> builder)
        where TEntity : AuditableEntity
    {
        builder.Property(entity => entity.CreateTime).HasColumnName("CREATE_TIME").HasColumnType("TIMESTAMP(6)").HasDefaultValueSql("CURRENT_TIMESTAMP").ValueGeneratedOnAdd();
        builder.Property(entity => entity.UpdateTime).HasColumnName("UPDATE_TIME").HasColumnType("TIMESTAMP(6)").HasDefaultValueSql("CURRENT_TIMESTAMP").ValueGeneratedOnAddOrUpdate();
        builder.Property(entity => entity.CreateBy).HasColumnName("CREATE_BY").HasColumnType("VARCHAR2(50 CHAR)").HasMaxLength(50).IsUnicode(false);
        builder.Property(entity => entity.UpdateBy).HasColumnName("UPDATE_BY").HasColumnType("VARCHAR2(50 CHAR)").HasMaxLength(50).IsUnicode(false);
    }
}
