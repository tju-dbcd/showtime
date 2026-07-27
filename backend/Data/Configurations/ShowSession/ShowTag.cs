using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShowtimeBackend.Entities.ShowSession;

namespace ShowtimeBackend.Data.Configurations.ShowSession
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

            // 单列查询索引 (IDX_SHOW_TAG_TAG)
            // 显式声明 FK 列索引与 DDL 命名一致，同时抑制 EF 默认 IX_SHOW_TAG_TAG_ID 自动索引
            builder.HasIndex(x => x.TagId)
                   .HasDatabaseName("IDX_SHOW_TAG_TAG");

            // 外键与级联删除配置 (FK_SHOW_TAG_SHOW & FK_SHOW_TAG_TAG)
            // 显式指定 inverse 导航集合，否则 EF 会因 Show.ShowTags / Tag.ShowTags 已存在
            // 而误判为另一条关系，自动生成影子 FK 列 ShowId1 / TagId1。
            builder.HasOne(x => x.Show)
                   .WithMany(x => x.ShowTags)
                   .HasForeignKey(x => x.ShowId)
                   .HasConstraintName("FK_SHOW_TAG_SHOW")
                   .OnDelete(DeleteBehavior.Cascade); // 对应 ON DELETE CASCADE

            builder.HasOne(x => x.Tag)
                   .WithMany(x => x.ShowTags)
                   .HasForeignKey(x => x.TagId)
                   .HasConstraintName("FK_SHOW_TAG_TAG")
                   .OnDelete(DeleteBehavior.Cascade); // 对应 ON DELETE CASCADE
        }
    }
}
