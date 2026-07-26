using ShowtimeBackend.Entities.ShowSessions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ShowtimeBackend.Data.Configurations.ShowSessions
{
    public class ShowConfiguration : IEntityTypeConfiguration<Show>
    {
        public void Configure(EntityTypeBuilder<Show> builder)
        {
            // 映射表名
            builder.ToTable("SHOW");

            // 主键配置
            builder.HasKey(s => s.ShowId);
            builder.Property(s => s.ShowId)
                   .HasColumnName("SHOW_ID")
                   .HasColumnType("NUMBER(19)")
                   .ValueGeneratedOnAdd(); // 对应自增列

            // 核心字段与长度限制配置
            builder.Property(s => s.ShowName)
                   .HasColumnName("SHOW_NAME")
                   .HasColumnType("VARCHAR2(200 CHAR)")
                   .HasMaxLength(200)
                   .IsRequired();

            builder.Property(s => s.CategoryId)
                   .HasColumnName("CATEGORY_ID")
                   .HasColumnType("NUMBER(19)")
                   .IsRequired();

            builder.Property(s => s.Description)
                   .HasColumnName("DESCRIPTION")
                   .HasColumnType("VARCHAR2(2000 CHAR)")
                   .HasMaxLength(2000)
                   .IsRequired(false); // 允许为空

            builder.Property(s => s.DurationMinutes)
                   .HasColumnName("DURATION_MINUTES")
                   .HasColumnType("NUMBER(5)")
                   .IsRequired(false);

            builder.Property(s => s.PosterUrl)
                   .HasColumnName("POSTER_URL")
                   .HasColumnType("VARCHAR2(500 CHAR)")
                   .HasMaxLength(500)
                   .IsRequired(false);

            // 状态与默认值配置
            builder.Property(s => s.Status)
                   .HasColumnName("STATUS")
                   .HasColumnType("VARCHAR2(20 CHAR)")
                   .HasMaxLength(20)
                   .HasDefaultValue("DRAFT")
                   .IsRequired();

            builder.Property(s => s.AuditStatus)
                   .HasColumnName("AUDIT_STATUS")
                   .HasColumnType("VARCHAR2(20 CHAR)")
                   .HasMaxLength(20)
                   .HasDefaultValue("PENDING")
                   .IsRequired();

            builder.Property(s => s.AuditBy)
                   .HasColumnName("AUDIT_BY")
                   .HasColumnType("VARCHAR2(50 CHAR)")
                   .HasMaxLength(50)
                   .IsRequired(false);

            builder.Property(s => s.AuditTime)
                   .HasColumnName("AUDIT_TIME")
                   .HasColumnType("TIMESTAMP(6)")
                   .IsRequired(false);

            // 审计字段配置
            builder.Property(s => s.CreateTime)
                   .HasColumnName("CREATE_TIME")
                   .HasColumnType("TIMESTAMP(6)")
                   .HasDefaultValueSql("CURRENT_TIMESTAMP")
                   .IsRequired();

            builder.Property(s => s.UpdateTime)
                   .HasColumnName("UPDATE_TIME")
                   .HasColumnType("TIMESTAMP(6)")
                   .HasDefaultValueSql("CURRENT_TIMESTAMP")
                   .IsRequired();

            builder.Property(s => s.CreateBy)
                   .HasColumnName("CREATE_BY")
                   .HasColumnType("VARCHAR2(50 CHAR)")
                   .HasMaxLength(50)
                   .IsRequired(false);

            builder.Property(s => s.UpdateBy)
                   .HasColumnName("UPDATE_BY")
                   .HasColumnType("VARCHAR2(50 CHAR)")
                   .HasMaxLength(50)
                   .IsRequired(false);

            // 外键关系映射（一对多：一个 Category 下有多场 Show）
            builder.HasOne(s => s.Category)
                   .WithMany() 
                   .HasForeignKey(s => s.CategoryId)
                   .OnDelete(DeleteBehavior.Restrict); // 禁止级联删除，保护主表数据
            builder.ConfigureAuditableEntity();
        }
    }
}
