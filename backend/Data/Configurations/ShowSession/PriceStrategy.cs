using ShowtimeBackend.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ShowtimeBackend.Data.Configurations
{
    public class PriceStrategyConfiguration : IEntityTypeConfiguration<PriceStrategy>
    {
        public void Configure(EntityTypeBuilder<PriceStrategy> builder)
        {
            // 映射表名
            builder.ToTable("PRICE_STRATEGY");

            // 主键配置
            builder.HasKey(ps => ps.PriceStrategyId);
            builder.Property(ps => ps.PriceStrategyId)
                   .HasColumnName("PRICE_STRATEGY_ID")
                   .HasColumnType("NUMBER(19)")
                   .ValueGeneratedOnAdd(); // 自增主键

            // 业务属性配置
            builder.Property(ps => ps.SessionId)
                   .HasColumnName("SESSION_ID")
                   .HasColumnType("NUMBER(19)")
                   .IsRequired();

            builder.Property(ps => ps.SeatSectionId)
                   .HasColumnName("SEAT_SECTION_ID")
                   .HasColumnType("NUMBER(19)")
                   .IsRequired();

            builder.Property(ps => ps.StrategyName)
                   .HasColumnName("STRATEGY_NAME")
                   .HasColumnType("VARCHAR2(100 CHAR)")
                   .HasMaxLength(100)
                   .IsRequired();

            builder.Property(ps => ps.PriceType)
                   .HasColumnName("PRICE_TYPE")
                   .HasColumnType("VARCHAR2(20 CHAR)")
                   .HasMaxLength(20)
                   .HasDefaultValue("STANDARD")
                   .IsRequired();

            // 金额类型必须显式设置 Precision 与 Scale，避免 EF Core 编译警告与精度丢失
            builder.Property(ps => ps.Price)
                   .HasColumnName("PRICE")
                   .HasColumnType("NUMBER(10,2)")
                   .HasPrecision(10, 2)
                   .HasDefaultValue(0.00m)
                   .IsRequired();

            builder.Property(ps => ps.SaleStartTime)
                   .HasColumnName("SALE_START_TIME")
                   .HasColumnType("TIMESTAMP(6)")
                   .IsRequired();

            builder.Property(ps => ps.SaleEndTime)
                   .HasColumnName("SALE_END_TIME")
                   .HasColumnType("TIMESTAMP(6)")
                   .IsRequired();

            builder.Property(ps => ps.Priority)
                   .HasColumnName("PRIORITY")
                   .HasColumnType("NUMBER(5)")
                   .HasDefaultValue(0)
                   .IsRequired();

            builder.Property(ps => ps.Quota)
                   .HasColumnName("QUOTA")
                   .HasColumnType("NUMBER(10)")
                   .IsRequired(false); // 可为空（不限量）

            builder.Property(ps => ps.Status)
                   .HasColumnName("STATUS")
                   .HasColumnType("VARCHAR2(20 CHAR)")
                   .HasMaxLength(20)
                   .HasDefaultValue("ENABLED")
                   .IsRequired();

            // 审计字段配置
            builder.Property(ps => ps.CreateTime)
                   .HasColumnName("CREATE_TIME")
                   .HasColumnType("TIMESTAMP(6)")
                   .HasDefaultValueSql("CURRENT_TIMESTAMP")
                   .IsRequired();

            builder.Property(ps => ps.UpdateTime)
                   .HasColumnName("UPDATE_TIME")
                   .HasColumnType("TIMESTAMP(6)")
                   .HasDefaultValueSql("CURRENT_TIMESTAMP")
                   .IsRequired();

            builder.Property(ps => ps.CreateBy)
                   .HasColumnName("CREATE_BY")
                   .HasColumnType("VARCHAR2(50 CHAR)")
                   .HasMaxLength(50)
                   .IsRequired(false);

            builder.Property(ps => ps.UpdateBy)
                   .HasColumnName("UPDATE_BY")
                   .HasColumnType("VARCHAR2(50 CHAR)")
                   .HasMaxLength(50)
                   .IsRequired(false);

            // 外键关系配置
            // 与 SESSION 表建立一对多关系
            builder.HasOne(ps => ps.Session)
                   .WithMany()
                   .HasForeignKey(ps => ps.SessionId)
                   .OnDelete(DeleteBehavior.Restrict);
            ///<summary>
            ///TODO: 这里可以考虑在 SeatSection 实体中添加一个集合属性来表示与 PriceStrategy 的一对多关系，这样可以更好地导航和管理相关数据。
            ///      等待收到座位分区设计方案后再决定是否需要在 SeatSection 实体中添加集合属性。
            ///</summary>>
            // 与 SEAT_SECTION 表建立一对多关系
            //builder.HasOne(ps => ps.SeatSection)
            //       .WithMany()
            //       .HasForeignKey(ps => ps.SeatSectionId)
            //       .OnDelete(DeleteBehavior.Restrict);
        }
    }
}
