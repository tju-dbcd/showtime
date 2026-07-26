using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShowtimeBackend.Entities.ShowSessions;

namespace ShowtimeBackend.Data.Configurations.ShowSessions
{
    public class ShowTagConfiguration : IEntityTypeConfiguration<ShowTag>
    {
        public void Configure(EntityTypeBuilder<ShowTag> builder)
        {
            // 设置目标表名
            builder.ToTable("SHOW_TAG");

            // 主键配置 (PK_SHOW_TAG)
            builder.HasKey(x => x.ShowTagId).HasName("PK_SHOW_TAG");
            builder.Property(x => x.ShowTagId)
                   .HasColumnName("SHOW_TAG_ID")
                   .HasColumnType("NUMBER(19,0)")
                   .ValueGeneratedOnAdd();

            // 基础外键字段映射
            builder.Property(x => x.ShowId)
                   .HasColumnName("SHOW_ID")
                   .HasColumnType("NUMBER(19,0)")
                   .IsRequired();

            builder.Property(x => x.TagId)
                   .HasColumnName("TAG_ID")
                   .HasColumnType("NUMBER(19,0)")
                   .IsRequired();

            // 复合唯一索引 (UK_SHOW_TAG)
            builder.HasIndex(x => new { x.ShowId, x.TagId })
                   .IsUnique()
                   .HasDatabaseName("UK_SHOW_TAG");

            // 外键与级联删除配置 (FK_SHOW_TAG_SHOW & FK_SHOW_TAG_TAG)
            builder.HasOne(x => x.Show)
                   .WithMany()
                   .HasForeignKey(x => x.ShowId)
                   .HasConstraintName("FK_SHOW_TAG_SHOW")
                   .OnDelete(DeleteBehavior.Cascade); // 对应 ON DELETE CASCADE

            builder.HasOne(x => x.Tag)
                   .WithMany()
                   .HasForeignKey(x => x.TagId)
                   .HasConstraintName("FK_SHOW_TAG_TAG")
                   .OnDelete(DeleteBehavior.Cascade); // 对应 ON DELETE CASCADE
        }
    }
}
