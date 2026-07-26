using ShowtimeBackend.Entities.ShowSessions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ShowtimeBackend.Data.Configurations.ShowSessions
{
    public class MarketingContentConfiguration : IEntityTypeConfiguration<MarketingContent>
    {
        public void Configure(EntityTypeBuilder<MarketingContent> builder)
        {
            // 映射表名
            builder.ToTable("MARKETING_CONTENT");

            // 主键配置
            builder.HasKey(mc => mc.ContentId);
            builder.Property(mc => mc.ContentId)
                   .HasColumnName("CONTENT_ID")
                   .HasColumnType("NUMBER(19)")
                   .ValueGeneratedOnAdd(); // 自增主键

            // 业务属性配置
            builder.Property(mc => mc.ShowId)
                   .HasColumnName("SHOW_ID")
                   .HasColumnType("NUMBER(19)")
                   .IsRequired();

            builder.Property(mc => mc.ContentType)
                   .HasColumnName("CONTENT_TYPE")
                   .HasColumnType("VARCHAR2(20 CHAR)")
                   .HasMaxLength(20)
                   .HasDefaultValue("NOTICE")
                   .IsRequired();

            builder.Property(mc => mc.Title)
                   .HasColumnName("TITLE")
                   .HasColumnType("VARCHAR2(200 CHAR)")
                   .HasMaxLength(200)
                   .IsRequired();

            // CLOB 字段映射为长文本
            builder.Property(mc => mc.ContentText)
                   .HasColumnName("CONTENT_TEXT")
                   .HasColumnType("CLOB")
                   .IsRequired(false);

            builder.Property(mc => mc.ImageUrl)
                   .HasColumnName("IMAGE_URL")
                   .HasColumnType("VARCHAR2(500 CHAR)")
                   .HasMaxLength(500)
                   .IsRequired(false);

            builder.Property(mc => mc.SortOrder)
                   .HasColumnName("SORT_ORDER")
                   .HasColumnType("NUMBER(5)")
                   .HasDefaultValue(0)
                   .IsRequired();

            builder.Property(mc => mc.Status)
                   .HasColumnName("STATUS")
                   .HasColumnType("VARCHAR2(20 CHAR)")
                   .HasMaxLength(20)
                   .HasDefaultValue("ENABLED")
                   .IsRequired();

            builder.Property(mc => mc.PublishTime)
                   .HasColumnName("PUBLISH_TIME")
                   .HasColumnType("TIMESTAMP(6)")
                   .IsRequired(false);

            // 审计字段配置
            builder.Property(mc => mc.CreateTime)
                   .HasColumnName("CREATE_TIME")
                   .HasColumnType("TIMESTAMP(6)")
                   .HasDefaultValueSql("CURRENT_TIMESTAMP")
                   .IsRequired();

            builder.Property(mc => mc.UpdateTime)
                   .HasColumnName("UPDATE_TIME")
                   .HasColumnType("TIMESTAMP(6)")
                   .HasDefaultValueSql("CURRENT_TIMESTAMP")
                   .IsRequired();

            builder.Property(mc => mc.CreateBy)
                   .HasColumnName("CREATE_BY")
                   .HasColumnType("VARCHAR2(50 CHAR)")
                   .HasMaxLength(50)
                   .IsRequired(false);

            builder.Property(mc => mc.UpdateBy)
                   .HasColumnName("UPDATE_BY")
                   .HasColumnType("VARCHAR2(50 CHAR)")
                   .HasMaxLength(50)
                   .IsRequired(false);

            // 外键关系配置
            builder.HasOne(mc => mc.Show)
                   .WithMany()
                   .HasForeignKey(mc => mc.ShowId)
                   .OnDelete(DeleteBehavior.Restrict);
        }
    }
}
