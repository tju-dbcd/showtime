using ShowtimeBackend.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ShowtimeBackend.Data.Configurations
{
    public class TagConfiguration : IEntityTypeConfiguration<Tag>
    {
        public void Configure(EntityTypeBuilder<Tag> builder)
        {
            // 映射表名
            builder.ToTable("TAG");

            // 主键配置
            builder.HasKey(t => t.TagId);
            builder.Property(t => t.TagId)
                   .HasColumnName("TAG_ID")
                   .HasColumnType("NUMBER(19)")
                   .ValueGeneratedOnAdd(); // 对应自增主键

            // 基础属性配置
            builder.Property(t => t.TagName)
                   .HasColumnName("TAG_NAME")
                   .HasColumnType("VARCHAR2(50 CHAR)")
                   .HasMaxLength(50)
                   .IsRequired();

            builder.Property(t => t.Color)
                   .HasColumnName("COLOR")
                   .HasColumnType("VARCHAR2(20 CHAR)")
                   .HasMaxLength(20)
                   .IsRequired(false); // 允许为空

            builder.Property(t => t.Status)
                   .HasColumnName("STATUS")
                   .HasColumnType("NUMBER(1)")
                   .HasDefaultValue(1)
                   .IsRequired();

            // 审计字段配置
            builder.Property(t => t.CreateTime)
                   .HasColumnName("CREATE_TIME")
                   .HasColumnType("TIMESTAMP(6)")
                   .HasDefaultValueSql("CURRENT_TIMESTAMP")
                   .IsRequired();

            builder.Property(t => t.UpdateTime)
                   .HasColumnName("UPDATE_TIME")
                   .HasColumnType("TIMESTAMP(6)")
                   .HasDefaultValueSql("CURRENT_TIMESTAMP")
                   .IsRequired();

            builder.Property(t => t.CreateBy)
                   .HasColumnName("CREATE_BY")
                   .HasColumnType("VARCHAR2(50 CHAR)")
                   .HasMaxLength(50)
                   .IsRequired(false);

            builder.Property(t => t.UpdateBy)
                   .HasColumnName("UPDATE_BY")
                   .HasColumnType("VARCHAR2(50 CHAR)")
                   .HasMaxLength(50)
                   .IsRequired(false);
        }
    }
}
