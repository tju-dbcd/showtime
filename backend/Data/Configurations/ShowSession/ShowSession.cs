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

            // TODO(PR #5 依赖): SeatMap 实体目前位于 Feature/SeatZoneMapping 分支，合并入 Develop 后启用：
            //   builder.HasOne<SeatMap>()
            //          .WithMany()
            //          .HasForeignKey(x => x.SeatMapId)
            //          .HasConstraintName("FK_SHOW_SESSION_SEAT_MAP")
            //          .OnDelete(DeleteBehavior.Restrict);
            // 当前 SeatMap 类型在本分支不可见，保持注释以避免编译错误；索引见下方已就绪。
            //builder.HasOne(x => x.SeatMap)
            //       .WithMany()
            //       .HasForeignKey(x => x.SeatMapId)
            //       .HasConstraintName("FK_SHOW_SESSION_SEAT_MAP");

            // 查询索引 (与 DDL 命名 100% 对齐)
            // IDX_SHOW_SESSION_SHOW / IDX_SHOW_SESSION_SEAT_MAP / IDX_SHOW_SESSION_STATUS / IDX_SHOW_SESSION_START
            // 显式声明 FK 列索引名，抑制 EF 默认 IX_SHOW_SESSION_SHOW_ID 自动索引
            builder.HasIndex(x => x.ShowId)
                   .HasDatabaseName("IDX_SHOW_SESSION_SHOW");
            builder.HasIndex(x => x.SeatMapId)
                   .HasDatabaseName("IDX_SHOW_SESSION_SEAT_MAP");
            builder.HasIndex(x => x.SessionStatus)
                   .HasDatabaseName("IDX_SHOW_SESSION_STATUS");
            builder.HasIndex(x => x.StartTime)
                   .HasDatabaseName("IDX_SHOW_SESSION_START");

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
