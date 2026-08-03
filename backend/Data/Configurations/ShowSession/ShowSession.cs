using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShowtimeBackend.Entities.SeatZone;
using ShowtimeBackend.Entities.ShowSession;

namespace ShowtimeBackend.Data.Configurations.ShowSession;

public class ShowSessionConfiguration : IEntityTypeConfiguration<Entities.ShowSession.ShowSession>
{
    public void Configure(EntityTypeBuilder<Entities.ShowSession.ShowSession> builder)
    {
        builder.ToTable("SHOW_SESSION", table =>
        {
            table.HasCheckConstraint("CK_SHOW_SESSION_STATUS", "SESSION_STATUS IN ('UPCOMING', 'PRESALE', 'ONSALE', 'SOLD_OUT', 'ENDED')");
            table.HasCheckConstraint("CK_SHOW_SESSION_TIME", "START_TIME < END_TIME AND SALE_START_TIME < SALE_END_TIME");
        });

        builder.HasKey(item => item.SessionId).HasName("PK_SHOW_SESSION");
        builder.Property(item => item.SessionId).HasColumnName("SESSION_ID").HasColumnType("NUMBER(19,0)").ValueGeneratedOnAdd();
        builder.Property(item => item.ShowId).HasColumnName("SHOW_ID").HasColumnType("NUMBER(19,0)").IsRequired();
        builder.Property(item => item.SeatMapId).HasColumnName("SEAT_MAP_ID").HasColumnType("NUMBER(19,0)").IsRequired();
        builder.Property(item => item.StartTime).HasColumnName("START_TIME").HasColumnType("TIMESTAMP(6)").IsRequired();
        builder.Property(item => item.EndTime).HasColumnName("END_TIME").HasColumnType("TIMESTAMP(6)").IsRequired();
        builder.Property(item => item.SaleStartTime).HasColumnName("SALE_START_TIME").HasColumnType("TIMESTAMP(6)").IsRequired();
        builder.Property(item => item.SaleEndTime).HasColumnName("SALE_END_TIME").HasColumnType("TIMESTAMP(6)").IsRequired();
        builder.Property(item => item.SessionStatus)
            .HasColumnName("SESSION_STATUS")
            .HasColumnType("VARCHAR2(20 CHAR)")
            .HasMaxLength(20)
            .HasDefaultValue("UPCOMING")
            .IsRequired();

        builder.HasOne(item => item.Show)
            .WithMany()
            .HasForeignKey(item => item.ShowId)
            .HasConstraintName("FK_SHOW_SESSION_SHOW");

        builder.HasOne<SeatMap>()
            .WithMany()
            .HasForeignKey(item => item.SeatMapId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_SHOW_SESSION_SEAT_MAP");

        builder.HasIndex(item => item.ShowId).HasDatabaseName("IDX_SHOW_SESSION_SHOW");
        builder.HasIndex(item => item.SeatMapId).HasDatabaseName("IDX_SHOW_SESSION_SEAT_MAP");
        builder.HasIndex(item => item.SessionStatus).HasDatabaseName("IDX_SHOW_SESSION_STATUS");
        builder.HasIndex(item => item.StartTime).HasDatabaseName("IDX_SHOW_SESSION_START");

        builder.ConfigureAuditableEntity();
    }
}
