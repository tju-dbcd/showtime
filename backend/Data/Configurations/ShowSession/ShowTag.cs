using ShowtimeBackend.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ShowtimeBackend.Data.Configurations.ShowSession
{
    public class ShowTagConfiguration : IEntityTypeConfiguration<ShowTag>
    {
        public void Configure(EntityTypeBuilder<ShowTag> builder)
        {
            // 设置表名
            builder.ToTable("SHOW_TAG");
            // 设置主键
            builder.HasKey(st => st.ShowTagId);
            // 设置索引
            builder.HasIndex(st => new { st.ShowId, st.TagId }).IsUnique();
            // 设置属性映射
            builder.Property(st => st.ShowTagId)
                .HasColumnName("SHOW_TAG_ID")
                .HasColumnType("NUMBER(19)")
                .ValueGeneratedOnAdd()
                .HasMaxLength(50)
                .IsRequired();
            builder.Property(st => st.ShowId)
                .HasColumnName("SHOW_ID")
                .HasColumnType("NUMBER(19)")
                .ValueGeneratedOnAdd()
                .HasMaxLength(50)
                .IsRequired();
            builder.Property(st => st.TagId)
                .HasColumnName("TAG_ID")
                .HasColumnType("NUMBER(19)")
                .ValueGeneratedOnAdd()
                .HasMaxLength(50)
                .IsRequired();
        }
    }
}
