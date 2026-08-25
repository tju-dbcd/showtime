using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShowtimeBackend.Entities.ShowSession;

namespace ShowtimeBackend.Data.Configurations.ShowSession;

public class PriceStrategyConfiguration : IEntityTypeConfiguration<PriceStrategy>
{
    public void Configure(EntityTypeBuilder<PriceStrategy> builder)
    {
        // 表名与 CHECK 约束 
        builder.ToTable("PRICE_STRATEGY", table =>
        {
            table.HasCheckConstraint("CK_PRICE_TYPE", "PRICE_TYPE IN ('EARLY_BIRD', 'PRESALE', 'STANDARD', 'VIP', 'MEMBER')");
            table.HasCheckConstraint("CK_PRICE_STATUS", "STATUS IN ('ENABLED', 'DISABLED')");
            table.HasCheckConstraint("CK_PRICE_VALUE", "PRICE >= 0");
        });

        // 主键配置 (PK_PRICE_STRATEGY)
        builder.HasKey(x => x.PriceStrategyId).HasName("PK_PRICE_STRATEGY");
        builder.Property(x => x.PriceStrategyId)
               .HasColumnName("PRICE_STRATEGY_ID")
               .HasColumnType("NUMBER(19,0)")
               .ValueGeneratedOnAdd();

        // 业务与时间字段映射
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
               .IsRequired();

        builder.Property(x => x.PriceType)
               .HasColumnName("PRICE_TYPE")
               .HasColumnType("VARCHAR2(20 CHAR)")
               .HasDefaultValueSql("'STANDARD'")
               .IsRequired();

        builder.Property(x => x.Price)
               .HasColumnName("PRICE")
               .HasColumnType("NUMBER(10,2)")
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
               .HasDefaultValueSql("'ENABLED'")
               .IsRequired();

        // 外键关联配置 (FK_PRICE_SESSION)
        builder.HasOne(x => x.ShowSession)
               .WithMany()
               .HasForeignKey(x => x.SessionId)
               .HasConstraintName("FK_PRICE_SESSION");

        // 索引配置,保留 DDL 中实际存在的单列索引,去除了先前的问题
        builder.HasIndex(x => x.SessionId)
               .HasDatabaseName("IDX_PRICE_SESSION");

        builder.HasIndex(x => x.SeatSectionId)
               .HasDatabaseName("IDX_PRICE_SECTION");

        // 在 Configure 方法追加：
        builder.HasOne(p => p.SeatSection)
               .WithMany()
               .HasForeignKey(p => p.SeatSectionId)
               .OnDelete(DeleteBehavior.Restrict);

        // 审计字段
        builder.ConfigureAuditableEntity();
    }
}
