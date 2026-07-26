using ShowtimeBackend.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ShowtimeBackend.Data.Configurations
{
    public class CategoryConfiguration : IEntityTypeConfiguration<Category>
    {
        public void Configure(EntityTypeBuilder<Category> builder)
        {
            // 映射表名
            builder.ToTable("CATEGORY");

            // 主键配置
            builder.HasKey(c => c.CategoryId);
            builder.Property(c => c.CategoryId)
                   .HasColumnName("CATEGORY_ID")
                   .HasColumnType("NUMBER(19)")
                   .ValueGeneratedOnAdd();

            // 基础属性配置
            builder.Property(c => c.CategoryName)
                   .HasColumnName("CATEGORY_NAME")
                   .HasColumnType("VARCHAR2(50 CHAR)")
                   .HasMaxLength(50)
                   .IsRequired();

            builder.Property(c => c.ParentId)
                   .HasColumnName("PARENT_ID")
                   .HasColumnType("NUMBER(19)")
                   .IsRequired(false); // 允许为空（顶级分类没有父级）

            builder.Property(c => c.SortOrder)
                   .HasColumnName("SORT_ORDER")
                   .HasColumnType("NUMBER(5)")
                   .HasDefaultValue(0)
                   .IsRequired(false);

            builder.Property(c => c.Status)
                   .HasColumnName("STATUS")
                   .HasColumnType("NUMBER(1)")
                   .HasDefaultValue(1)
                   .IsRequired();

            // 审计字段配置
            builder.Property(c => c.CreateTime)
                   .HasColumnName("CREATE_TIME")
                   .HasColumnType("TIMESTAMP(6)")
                   .HasDefaultValueSql("CURRENT_TIMESTAMP")
                   .IsRequired();

            builder.Property(c => c.UpdateTime)
                   .HasColumnName("UPDATE_TIME")
                   .HasColumnType("TIMESTAMP(6)")
                   .HasDefaultValueSql("CURRENT_TIMESTAMP")
                   .IsRequired();

            builder.Property(c => c.CreateBy)
                   .HasColumnName("CREATE_BY")
                   .HasColumnType("VARCHAR2(50 CHAR)")
                   .HasMaxLength(50)
                   .IsRequired(false);

            builder.Property(c => c.UpdateBy)
                   .HasColumnName("UPDATE_BY")
                   .HasColumnType("VARCHAR2(50 CHAR)")
                   .HasMaxLength(50)
                   .IsRequired(false);

            // 自关联树形外键配置 (Self-Referencing Relationship)
            builder.HasOne(c => c.Parent)
                   .WithMany(c => c.Children)
                   .HasForeignKey(c => c.ParentId)
                   .OnDelete(DeleteBehavior.Restrict); // 删除父分类时，阻止直接级联删除子分类，保护分类树安全
        }
    }
}
