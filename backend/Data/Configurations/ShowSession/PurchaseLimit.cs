using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShowtimeBackend.Entities.ShowSession;

namespace ShowtimeBackend.Data.Configurations.ShowSession
{
    public class PurchaseLimitConfiguration : IEntityTypeConfiguration<PurchaseLimit>
    {
        public void Configure(EntityTypeBuilder<PurchaseLimit> builder)
        {
            // 设置表名与 4 项 CHECK 约束
            builder.ToTable("PURCHASE_LIMIT", t =>
            {
                t.HasCheckConstraint("CK_LIMIT_CHANNEL", "CHANNEL IN ('WEB', 'APP', 'MINI_PROGRAM')");
                t.HasCheckConstraint("CK_LIMIT_USER_TYPE", "USER_TYPE IN ('NORMAL', 'MEMBER', 'VIP')");
                t.HasCheckConstraint("CK_LIMIT_TYPE", "LIMIT_TYPE IN ('TICKET', 'ORDER')");
                t.HasCheckConstraint("CK_LIMIT_STATUS", "STATUS IN ('ENABLED', 'DISABLED')");
            });

            // 主键配置 (PK_PURCHASE_LIMIT)
            builder.HasKey(x => x.LimitId).HasName("PK_PURCHASE_LIMIT");
            builder.Property(x => x.LimitId)
                   .HasColumnName("LIMIT_ID")
                   .HasColumnType("NUMBER(19,0)")
                   .ValueGeneratedOnAdd();

            // 业务字段映射
            builder.Property(x => x.LimitName)
                   .HasColumnName("LIMIT_NAME")
                   .HasColumnType("VARCHAR2(100 CHAR)")
                   .HasMaxLength(100)
                   .IsRequired();

            builder.Property(x => x.ShowId)
                   .HasColumnName("SHOW_ID")
                   .HasColumnType("NUMBER(19,0)")
                   .IsRequired(false);

            builder.Property(x => x.SessionId)
                   .HasColumnName("SESSION_ID")
                   .HasColumnType("NUMBER(19,0)")
                   .IsRequired(false);

            builder.Property(x => x.Channel)
                   .HasColumnName("CHANNEL")
                   .HasColumnType("VARCHAR2(20 CHAR)")
                   .HasMaxLength(20)
                   .IsRequired(false);

            builder.Property(x => x.UserType)
                   .HasColumnName("USER_TYPE")
                   .HasColumnType("VARCHAR2(20 CHAR)")
                   .HasMaxLength(20)
                   .IsRequired(false);

            builder.Property(x => x.MaxBuyCount)
                   .HasColumnName("MAX_BUY_COUNT")
                   .HasColumnType("NUMBER(5,0)")
                   .IsRequired();

            builder.Property(x => x.LimitType)
                   .HasColumnName("LIMIT_TYPE")
                   .HasColumnType("VARCHAR2(20 CHAR)")
                   .HasMaxLength(20)
                   .HasDefaultValue("TICKET")
                   .IsRequired();

            builder.Property(x => x.StartTime)
                   .HasColumnName("START_TIME")
                   .HasColumnType("TIMESTAMP(6)")
                   .IsRequired(false);

            builder.Property(x => x.EndTime)
                   .HasColumnName("END_TIME")
                   .HasColumnType("TIMESTAMP(6)")
                   .IsRequired(false);

            builder.Property(x => x.Status)
                   .HasColumnName("STATUS")
                   .HasColumnType("VARCHAR2(20 CHAR)")
                   .HasMaxLength(20)
                   .HasDefaultValue("ENABLED")
                   .IsRequired();

            // 外键配置 (FK_LIMIT_SHOW 与 FK_LIMIT_SESSION)
            builder.HasOne(x => x.Show)
                   .WithMany()
                   .HasForeignKey(x => x.ShowId)
                   .HasConstraintName("FK_LIMIT_SHOW")
                   .IsRequired(false);

            builder.HasOne(x => x.ShowSession)
                   .WithMany()
                   .HasForeignKey(x => x.SessionId)
                   .HasConstraintName("FK_LIMIT_SESSION")
                   .IsRequired(false);

            // 查询索引 (与 DDL 命名对齐, 同时显式抑制 EF 默认 IX 自动索引)
            // IDX_LIMIT_SHOW / IDX_LIMIT_SESSION
            builder.HasIndex(x => x.ShowId)
                   .HasDatabaseName("IDX_LIMIT_SHOW");
            builder.HasIndex(x => x.SessionId)
                   .HasDatabaseName("IDX_LIMIT_SESSION");

            // 审计字段映射 (AuditableEntity)
            builder.ConfigureAuditableEntity();
        }
    }
}
