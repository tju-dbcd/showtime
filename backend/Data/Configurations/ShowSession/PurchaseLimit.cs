using ShowtimeBackend.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ShowtimeBackend.Data.Configurations
{
    public class PurchaseLimitConfiguration : IEntityTypeConfiguration<PurchaseLimit>
    {
        public void Configure(EntityTypeBuilder<PurchaseLimit> builder)
        {
            // 映射表名
            builder.ToTable("PURCHASE_LIMIT");

            // 主键配置
            builder.HasKey(pl => pl.LimitId);
            builder.Property(pl => pl.LimitId)
                   .HasColumnName("LIMIT_ID")
                   .HasColumnType("NUMBER(19)")
                   .ValueGeneratedOnAdd(); // 自增主键

            // 业务属性配置
            builder.Property(pl => pl.LimitName)
                   .HasColumnName("LIMIT_NAME")
                   .HasColumnType("VARCHAR2(100 CHAR)")
                   .HasMaxLength(100)
                   .IsRequired();

            // 可空外键列
            builder.Property(pl => pl.ShowId)
                   .HasColumnName("SHOW_ID")
                   .HasColumnType("NUMBER(19)")
                   .IsRequired(false);

            builder.Property(pl => pl.SessionId)
                   .HasColumnName("SESSION_ID")
                   .HasColumnType("NUMBER(19)")
                   .IsRequired(false);

            builder.Property(pl => pl.Channel)
                   .HasColumnName("CHANNEL")
                   .HasColumnType("VARCHAR2(20 CHAR)")
                   .HasMaxLength(20)
                   .IsRequired(false);

            builder.Property(pl => pl.UserType)
                   .HasColumnName("USER_TYPE")
                   .HasColumnType("VARCHAR2(20 CHAR)")
                   .HasMaxLength(20)
                   .IsRequired(false);

            builder.Property(pl => pl.MaxBuyCount)
                   .HasColumnName("MAX_BUY_COUNT")
                   .HasColumnType("NUMBER(5)")
                   .HasDefaultValue(1)
                   .IsRequired();

            builder.Property(pl => pl.LimitType)
                   .HasColumnName("LIMIT_TYPE")
                   .HasColumnType("VARCHAR2(20 CHAR)")
                   .HasMaxLength(20)
                   .HasDefaultValue("TICKET")
                   .IsRequired();

            // 生效/失效时间配置（可为空）
            builder.Property(pl => pl.StartTime)
                   .HasColumnName("START_TIME")
                   .HasColumnType("TIMESTAMP(6)")
                   .IsRequired(false);

            builder.Property(pl => pl.EndTime)
                   .HasColumnName("END_TIME")
                   .HasColumnType("TIMESTAMP(6)")
                   .IsRequired(false);

            builder.Property(pl => pl.Status)
                   .HasColumnName("STATUS")
                   .HasColumnType("VARCHAR2(20 CHAR)")
                   .HasMaxLength(20)
                   .HasDefaultValue("ENABLED")
                   .IsRequired();

            // 审计字段配置
            builder.Property(pl => pl.CreateTime)
                   .HasColumnName("CREATE_TIME")
                   .HasColumnType("TIMESTAMP(6)")
                   .HasDefaultValueSql("CURRENT_TIMESTAMP")
                   .IsRequired();

            builder.Property(pl => pl.UpdateTime)
                   .HasColumnName("UPDATE_TIME")
                   .HasColumnType("TIMESTAMP(6)")
                   .HasDefaultValueSql("CURRENT_TIMESTAMP")
                   .IsRequired();

            builder.Property(pl => pl.CreateBy)
                   .HasColumnName("CREATE_BY")
                   .HasColumnType("VARCHAR2(50 CHAR)")
                   .HasMaxLength(50)
                   .IsRequired(false);

            builder.Property(pl => pl.UpdateBy)
                   .HasColumnName("UPDATE_BY")
                   .HasColumnType("VARCHAR2(50 CHAR)")
                   .HasMaxLength(50)
                   .IsRequired(false);

            // 外键关系配置 (可选外键 HasOne -> WithMany)
            builder.HasOne(pl => pl.Show)
                   .WithMany()
                   .HasForeignKey(pl => pl.ShowId)
                   .OnDelete(DeleteBehavior.Restrict);

            builder.HasOne(pl => pl.Session)
                   .WithMany()
                   .HasForeignKey(pl => pl.SessionId)
                   .OnDelete(DeleteBehavior.Restrict);
        }
    }
}
