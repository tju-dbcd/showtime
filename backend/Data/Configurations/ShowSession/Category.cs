using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShowtimeBackend.Entities.ShowSessions;

namespace ShowtimeBackend.Data.Configurations.ShowSessions
{
    public class CategoryConfiguration : IEntityTypeConfiguration<Category>
    {
        public void Configure(EntityTypeBuilder<Category> builder)
        {
            // 设置表名与 CHECK 约束 (CK_CATEGORY_STATUS)
            builder.ToTable("CATEGORY", t =>
            {
                t.HasCheckConstraint("CK_CATEGORY_STATUS", "STATUS IN (0, 1)");
            });

            // 主键配置 (PK_CATEGORY)
            builder.HasKey(x => x.CategoryId).HasName("PK_CATEGORY");
            builder.Property(x => x.CategoryId)
                   .HasColumnName("CATEGORY_ID")
                   .HasColumnType("NUMBER(19,0)")
                   .ValueGeneratedOnAdd();

            // 唯一索引配置 (UK_CATEGORY_NAME)
            builder.HasIndex(x => x.CategoryName)
                   .IsUnique()
                   .HasDatabaseName("UK_CATEGORY_NAME");

            // 业务字段映射
            builder.Property(x => x.CategoryName)
                   .HasColumnName("CATEGORY_NAME")
                   .HasColumnType("VARCHAR2(50 CHAR)")
                   .HasMaxLength(50)
                   .IsRequired();

            builder.Property(x => x.ParentId)
                   .HasColumnName("PARENT_ID")
                   .HasColumnType("NUMBER(19,0)")
                   .IsRequired(false);

            builder.Property(x => x.SortOrder)
                   .HasColumnName("SORT_ORDER")
                   .HasColumnType("NUMBER(5,0)")
                   .HasDefaultValue(0)
                   .IsRequired();

            builder.Property(x => x.Status)
                   .HasColumnName("STATUS")
                   .HasColumnType("NUMBER(1,0)")
                   .HasDefaultValue(1)
                   .IsRequired();

            // 自引用外键配置 (FK_CATEGORY_PARENT)
            builder.HasOne(x => x.ParentCategory)
                   .WithMany(x => x.SubCategories)
                   .HasForeignKey(x => x.ParentId)
                   .HasConstraintName("FK_CATEGORY_PARENT")
                   .OnDelete(DeleteBehavior.Restrict); // 避免删父级时产生级联删除

            // 审计字段映射 (AuditableEntity)
            builder.ConfigureAuditableEntity();
        }
    }
}
