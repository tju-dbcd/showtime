using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShowtimeBackend.Entities.ShowSession;

namespace ShowtimeBackend.Data.Configurations.ShowSession
{
    public class TagConfiguration : IEntityTypeConfiguration<Tag>
    {
        public void Configure(EntityTypeBuilder<Tag> builder)
        {
            // 设置表名与 CHECK 约束 (CK_TAG_STATUS)
            builder.ToTable("TAG", t =>
            {
                t.HasCheckConstraint("CK_TAG_STATUS", "STATUS IN (0, 1)");
            });

            // 主键配置 (PK_TAG)
            builder.HasKey(x => x.TagId).HasName("PK_TAG");
            builder.Property(x => x.TagId)
                   .HasColumnName("TAG_ID")
                   .HasColumnType("NUMBER(19,0)")
                   .ValueGeneratedOnAdd();

            // 唯一索引配置 (UK_TAG_NAME)
            builder.HasIndex(x => x.TagName)
                   .IsUnique()
                   .HasDatabaseName("UK_TAG_NAME");

            // 业务字段映射
            builder.Property(x => x.TagName)
                   .HasColumnName("TAG_NAME")
                   .HasColumnType("VARCHAR2(50 CHAR)")
                   .HasMaxLength(50)
                   .IsRequired();

            builder.Property(x => x.Color)
                   .HasColumnName("COLOR")
                   .HasColumnType("VARCHAR2(20 CHAR)")
                   .HasMaxLength(20)
                   .IsRequired(false);

            builder.Property(x => x.Status)
                   .HasColumnName("STATUS")
                   // NUMBER(3,0)：同 Category.Status，避免 NUMBER(1) bool 映射冲突
                   .HasColumnType("NUMBER(3,0)")
                   .HasDefaultValue(1)
                   .IsRequired();

            // 审计字段映射 (AuditableEntity)
            builder.ConfigureAuditableEntity();
        }
    }
}
