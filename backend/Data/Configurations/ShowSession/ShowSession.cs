using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShowtimeBackend.Entities.ShowSessions;

namespace ShowtimeBackend.Data.Configurations.ShowSessions
{
    public class ShowSessionConfiguration : IEntityTypeConfiguration<Entities.ShowSessions.ShowSession>
    {
        public void Configure(EntityTypeBuilder<Entities.ShowSessions.ShowSession> builder)
        {
            //  表名与 2 项 CHECK 约束（状态与时间区间校验）
            builder.ToTable("SHOW_SESSION", t =>
            {
                t.HasCheckConstraint("CK_SHOW_SESSION_STATUS", "SESSION_STATUS IN ('UPCOMING', 'PRESALE', 'ONSALE', 'SOLD_OUT', 'ENDED')");
                t.HasCheckConstraint("CK_SHOW_SESSION_TIME", "START_TIME < END_TIME AND SALE_START_TIME < SALE_END_TIME");
            });

            // 主键配置 (PK_SHOW_SESSION)
            builder.HasKey(x => x.SessionId).HasName("PK_SHOW_SESSION");
            builder.Property(x => x.SessionId)
                   .HasColumnName("SESSION_ID")
                   .HasColumnType("NUMBER(19,0)")
                   .ValueGeneratedOnAdd();

            // 业务与时间字段映射
            builder.Property(x => x.ShowId)
                   .HasColumnName("SHOW_ID")
                   .HasColumnType("NUMBER(19,0)")
                   .IsRequired();

            builder.Property(x => x.SeatMapId)
                   .HasColumnName("SEAT_MAP_ID")
                   .HasColumnType("NUMBER(19,0)")
                   .IsRequired();

            builder.Property(x => x.StartTime)
                   .HasColumnName("START_TIME")
                   .HasColumnType("TIMESTAMP(6)")
                   .IsRequired();

            builder.Property(x => x.EndTime)
                   .HasColumnName("END_TIME")
                   .HasColumnType("TIMESTAMP(6)")
                   .IsRequired();

            builder.Property(x => x.SaleStartTime)
                   .HasColumnName("SALE_START_TIME")
                   .HasColumnType("TIMESTAMP(6)")
                   .IsRequired();

            builder.Property(x => x.SaleEndTime)
                   .HasColumnName("SALE_END_TIME")
                   .HasColumnType("TIMESTAMP(6)")
                   .IsRequired();

            builder.Property(x => x.SessionStatus)
                   .HasColumnName("SESSION_STATUS")
                   .HasColumnType("VARCHAR2(20 CHAR)")
                   .HasMaxLength(20)
                   .HasDefaultValue("UPCOMING")
                   .IsRequired();

            // 外键关联配置 (FK_SHOW_SESSION_SHOW 与 FK_SHOW_SESSION_SEAT_MAP)
            builder.HasOne(x => x.Show)
                   .WithMany()
                   .HasForeignKey(x => x.ShowId)
                   .HasConstraintName("FK_SHOW_SESSION_SHOW");

            //builder.HasOne(x => x.SeatMap)
            //       .WithMany()
            //       .HasForeignKey(x => x.SeatMapId)
            //       .HasConstraintName("FK_SHOW_SESSION_SEAT_MAP");

            // 审计字段映射 (AuditableEntity)
            builder.ConfigureAuditableEntity();

            // SHOW_ID, SESSION_STATUS, START_TIME 复合索引
            builder.HasIndex(s => new { s.ShowId, s.SessionStatus, s.StartTime })
                   .HasDatabaseName("IDX_SHOW_SESSION_SHOW_STATUS");

            // 映射开售/停售状态扫描复合索引
            builder.HasIndex(s => new { s.SaleStartTime, s.SaleEndTime, s.SessionStatus })
                   .HasDatabaseName("IDX_SHOW_SESSION_SALE_TIME");
        }
    }
}
