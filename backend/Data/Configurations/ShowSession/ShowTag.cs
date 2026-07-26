using ShowtimeBackend.Entities.ShowSessions;
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
            builder.HasKey(st => st.ShowTagId)
                   .HasName("PK_SHOW_TAG");
            // 设置索引
            builder.HasIndex(st => new { st.ShowId, st.TagId }).IsUnique();
            // 设置属性映射
            builder.Property(st => st.ShowTagId)
                .HasColumnName("SHOW_TAG_ID")
                .ValueGeneratedOnAdd();
            builder.Property(st => st.ShowId)
                .HasColumnName("SHOW_ID")
                .IsRequired();
            builder.Property(st => st.TagId)
                .HasColumnName("TAG_ID")
                .IsRequired();

            builder.HasOne(x => x.Show)
                   .WithMany(s => s.ShowTags) // 或 WithMany()，按 Entity 中定义的集合导航属性调整
                   .HasForeignKey(x => x.ShowId)
                   .HasConstraintName("FK_SHOW_TAG_SHOW")
                   .OnDelete(DeleteBehavior.Cascade);
            builder.HasOne(x => x.Tag)
                   .WithMany(t => t.ShowTags) // 或 WithMany()，按 Entity 中定义的集合导航属性调整
                   .HasForeignKey(x => x.TagId)
                   .HasConstraintName("FK_SHOW_TAG_TAG")
                   .OnDelete(DeleteBehavior.Cascade);
        }
    }
}
