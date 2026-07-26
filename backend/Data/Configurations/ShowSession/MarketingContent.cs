using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShowtimeBackend.Entities.ShowSessions;

namespace ShowtimeBackend.Data.Configurations.ShowSessions
{
    public class MarketingContentConfiguration : IEntityTypeConfiguration<MarketingContent>
    {
        public void Configure(EntityTypeBuilder<MarketingContent> builder)
        {
            // 设置表名与双重 CHECK 约束 (CK_MARKETING_TYPE 与 CK_MARKETING_STATUS)
            builder.ToTable("MARKETING_CONTENT", t =>
            {
                t.HasCheckConstraint("CK_MARKETING_TYPE", "CONTENT_TYPE IN ('NOTICE', 'AD', 'PROMOTION')");
                t.HasCheckConstraint("CK_MARKETING_STATUS", "STATUS IN ('ENABLED', 'DISABLED')");
            });

            // 主键配置 (PK_MARKETING_CONTENT)
            builder.HasKey(x => x.ContentId).HasName("PK_MARKETING_CONTENT");
            builder.Property(x => x.ContentId)
                   .HasColumnName("CONTENT_ID")
                   .HasColumnType("NUMBER(19,0)")
                   .ValueGeneratedOnAdd();

            // 业务与属性字段映射
            builder.Property(x => x.ShowId)
                   .HasColumnName("SHOW_ID")
                   .HasColumnType("NUMBER(19,0)")
                   .IsRequired();

            builder.Property(x => x.ContentType)
                   .HasColumnName("CONTENT_TYPE")
                   .HasColumnType("VARCHAR2(20 CHAR)")
                   .HasMaxLength(20)
                   .HasDefaultValue("NOTICE")
                   .IsRequired();

            builder.Property(x => x.Title)
                   .HasColumnName("TITLE")
                   .HasColumnType("VARCHAR2(200 CHAR)")
                   .HasMaxLength(200)
                   .IsRequired();

            // CLOB 大文本映射
            builder.Property(x => x.ContentText)
                   .HasColumnName("CONTENT_TEXT")
                   .HasColumnType("CLOB")
                   .IsRequired(false);

            builder.Property(x => x.ImageUrl)
                   .HasColumnName("IMAGE_URL")
                   .HasColumnType("VARCHAR2(500 CHAR)")
                   .HasMaxLength(500)
                   .IsRequired(false);

            builder.Property(x => x.SortOrder)
                   .HasColumnName("SORT_ORDER")
                   .HasColumnType("NUMBER(5,0)")
                   .HasDefaultValue(0)
                   .IsRequired();

            builder.Property(x => x.Status)
                   .HasColumnName("STATUS")
                   .HasColumnType("VARCHAR2(20 CHAR)")
                   .HasMaxLength(20)
                   .HasDefaultValue("ENABLED")
                   .IsRequired();

            builder.Property(x => x.PublishTime)
                   .HasColumnName("PUBLISH_TIME")
                   .HasColumnType("TIMESTAMP(6)")
                   .IsRequired(false);

            // 外键配置 (FK_MARKETING_SHOW)
            builder.HasOne(x => x.Show)
                   .WithMany()
                   .HasForeignKey(x => x.ShowId)
                   .HasConstraintName("FK_MARKETING_SHOW");

            // 审计字段映射 (AuditableEntity)
            builder.ConfigureAuditableEntity();
        }
    }
}
