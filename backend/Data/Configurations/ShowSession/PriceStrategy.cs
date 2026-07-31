using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShowtimeBackend.Entities.ShowSessions;

namespace ShowtimeBackend.Data.Configurations.ShowSessions
{
    public class PriceStrategyConfiguration : IEntityTypeConfiguration<PriceStrategy>
    {
        public void Configure(EntityTypeBuilder<PriceStrategy> builder)
        {
            // 设置表名与 3 项 CHECK 约束
            builder.ToTable("PRICE_STRATEGY", t =>
            {
                t.HasCheckConstraint("CK_PRICE_TYPE", "PRICE_TYPE IN ('EARLY_BIRD', 'PRESALE', 'STANDARD', 'VIP', 'MEMBER')");
                t.HasCheckConstraint("CK_PRICE_STATUS", "STATUS IN ('ENABLED', 'DISABLED')");
                t.HasCheckConstraint("CK_PRICE_VALUE", "PRICE >= 0");
            });

            // 主键配置 (PK_PRICE_STRATEGY)
            builder.HasKey(x => x.PriceStrategyId).HasName("PK_PRICE_STRATEGY");
            builder.Property(x => x.PriceStrategyId)
                   .HasColumnName("PRICE_STRATEGY_ID")
                   .HasColumnType("NUMBER(19,0)")
                   .ValueGeneratedOnAdd();

            // 业务与属性字段映射
            builder.Property(x => x.SessionId)
                   .HasColumnName("SESSION_ID")
                   .HasColumnType("NUMBER(19,0)")
                   .IsRequired();

            builder.Property(x => x.SeatSectionId)
                   .HasColumnName("SEAT_SECTION_ID")
                   .HasColumnType("NUMBER(19,0)")
                   .IsRequired();

            builder.Property(x => x.StrategyName)
                   .HasColumnName("STRATEGY_NAME")
                   .HasColumnType("VARCHAR2(100 CHAR)")
                   .HasMaxLength(100)
                   .IsRequired();

            builder.Property(x => x.PriceType)
                   .HasColumnName("PRICE_TYPE")
                   .HasColumnType("VARCHAR2(20 CHAR)")
                   .HasMaxLength(20)
                   .HasDefaultValue("STANDARD")
                   .IsRequired();

            builder.Property(x => x.Price)
                   .HasColumnName("PRICE")
                   .HasColumnType("NUMBER(10,2)")
                   .HasPrecision(10, 2)
                   .IsRequired();

            builder.Property(x => x.SaleStartTime)
                   .HasColumnName("SALE_START_TIME")
                   .HasColumnType("TIMESTAMP(6)")
                   .IsRequired();

            builder.Property(x => x.SaleEndTime)
                   .HasColumnName("SALE_END_TIME")
                   .HasColumnType("TIMESTAMP(6)")
                   .IsRequired();

            builder.Property(x => x.Priority)
                   .HasColumnName("PRIORITY")
                   .HasColumnType("NUMBER(5,0)")
                   .HasDefaultValue(0)
                   .IsRequired();

            builder.Property(x => x.Quota)
                   .HasColumnName("QUOTA")
                   .HasColumnType("NUMBER(10,0)")
                   .IsRequired(false);

            builder.Property(x => x.Status)
                   .HasColumnName("STATUS")
                   .HasColumnType("VARCHAR2(20 CHAR)")
                   .HasMaxLength(20)
                   .HasDefaultValue("ENABLED")
                   .IsRequired();

            // 外键配置 (FK_PRICE_SESSION 与 FK_PRICE_SEAT_SECTION)
            builder.HasOne(x => x.ShowSession)
                   .WithMany()
                   .HasForeignKey(x => x.SessionId)
                   .HasConstraintName("FK_PRICE_SESSION");

            //builder.HasOne(x => x.SeatSection)
            //       .WithMany()
            //       .HasForeignKey(x => x.SeatSectionId)
            //       .HasConstraintName("FK_PRICE_SEAT_SECTION");

            // 审计字段映射 (AuditableEntity)
            builder.ConfigureAuditableEntity();

            // 映射选座实时计价复合索引
            builder.HasIndex(p => new { p.SessionId, p.SeatSectionId, p.Status })
                   .HasDatabaseName("IDX_PRICE_STRATEGY_SESS_SEC");

            // 映射场次 + 区域 + 票价类型
            builder.HasIndex(p => new { p.SessionId, p.SeatSectionId, p.PriceType })
                   .IsUnique()
                   .HasDatabaseName("UK_PRICE_STRATEGY_BIZ");
        }
    }
}
