using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShowtimeBackend.Entities.ShowSession;

namespace ShowtimeBackend.Data.Configurations.ShowSession
{
    public class ShowConfiguration : IEntityTypeConfiguration<Show>
    {
        public void Configure(EntityTypeBuilder<Show> builder)
        {
            // 设置目标表名与两项 CHECK 约束（与 DDL 名称及条件 100% 对齐）
            builder.ToTable("SHOW", t =>
            {
                t.HasCheckConstraint("CK_SHOW_STATUS", "STATUS IN ('DRAFT', 'PUBLISHED', 'UNPUBLISHED')");
                t.HasCheckConstraint("CK_SHOW_AUDIT", "AUDIT_STATUS IN ('PENDING', 'APPROVED', 'REJECTED')");
            });

            // 主键配置 (PK_SHOW)
            builder.HasKey(x => x.ShowId).HasName("PK_SHOW");
            builder.Property(x => x.ShowId)
                   .HasColumnName("SHOW_ID")
                   .HasColumnType("NUMBER(19,0)")
                   .ValueGeneratedOnAdd();

            // 基础业务字段映射
            builder.Property(x => x.ShowName)
                   .HasColumnName("SHOW_NAME")
                   .HasColumnType("VARCHAR2(200 CHAR)")
                   .HasMaxLength(200)
                   .IsRequired();

            builder.Property(x => x.CategoryId)
                   .HasColumnName("CATEGORY_ID")
                   .HasColumnType("NUMBER(19,0)")
                   .IsRequired();

            builder.Property(x => x.Description)
                   .HasColumnName("DESCRIPTION")
                   .HasColumnType("VARCHAR2(2000 CHAR)")
                   .HasMaxLength(2000)
                   .IsRequired(false);

            builder.Property(x => x.DurationMinutes)
                   .HasColumnName("DURATION_MINUTES")
                   .HasColumnType("NUMBER(5,0)")
                   .IsRequired(false);

            builder.Property(x => x.PosterUrl)
                   .HasColumnName("POSTER_URL")
                   .HasColumnType("VARCHAR2(500 CHAR)")
                   .HasMaxLength(500)
                   .IsRequired(false);

            // 状态与审核字段配置（包含默认值与长度约束）
            builder.Property(x => x.Status)
                   .HasColumnName("STATUS")
                   .HasColumnType("VARCHAR2(20 CHAR)")
                   .HasMaxLength(20)
                   .HasDefaultValue("DRAFT")
                   .IsRequired();

            builder.Property(x => x.AuditStatus)
                   .HasColumnName("AUDIT_STATUS")
                   .HasColumnType("VARCHAR2(20 CHAR)")
                   .HasMaxLength(20)
                   .HasDefaultValue("PENDING")
                   .IsRequired();

            builder.Property(x => x.AuditBy)
                   .HasColumnName("AUDIT_BY")
                   .HasColumnType("VARCHAR2(50 CHAR)")
                   .HasMaxLength(50)
                   .IsRequired(false);

            builder.Property(x => x.AuditTime)
                   .HasColumnName("AUDIT_TIME")
                   .HasColumnType("TIMESTAMP(6)")
                   .IsRequired(false);

            // 外键配置 (FK_SHOW_CATEGORY)
            builder.HasOne(x => x.Category)
                   .WithMany()
                   .HasForeignKey(x => x.CategoryId)
                   .HasConstraintName("FK_SHOW_CATEGORY");

            // 查询索引 (与 DDL 命名 100% 对齐)
            // IDX_SHOW_CATEGORY / IDX_SHOW_STATUS / IDX_SHOW_AUDIT
            // 显式声明 FK 列索引名，抑制 EF 默认 IX_SHOW_CATEGORY_ID 自动索引
            builder.HasIndex(x => x.CategoryId)
                   .HasDatabaseName("IDX_SHOW_CATEGORY");
            builder.HasIndex(x => x.Status)
                   .HasDatabaseName("IDX_SHOW_STATUS");
            builder.HasIndex(x => x.AuditStatus)
                   .HasDatabaseName("IDX_SHOW_AUDIT");

            // 复用 4 个标准审计字段（CREATE_TIME, UPDATE_TIME, CREATE_BY, UPDATE_BY）
            builder.ConfigureAuditableEntity();
        }
    }
}
