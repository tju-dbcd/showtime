using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShowtimeBackend.Entities.ShowSessions;

namespace ShowtimeBackend.Data.Configurations.ShowSessions
{
    public class VenueConfiguration : IEntityTypeConfiguration<Venue>
    {
        public void Configure(EntityTypeBuilder<Venue> builder)
        {
            // 设置表名与 CHECK 约束（与 DDL 中的 CK_VENUE_STATUS 保持一致）
            builder.ToTable("VENUE", t =>
            {
                t.HasCheckConstraint("CK_VENUE_STATUS", "STATUS IN ('ENABLED', 'DISABLED')");
            });

            // 主键配置 (PK_VENUE)
            builder.HasKey(x => x.VenueId).HasName("PK_VENUE");
            builder.Property(x => x.VenueId)
                   .HasColumnName("VENUE_ID")
                   .HasColumnType("NUMBER(19,0)")
                   .ValueGeneratedOnAdd();

            // 唯一索引配置 (UK_VENUE_NAME)
            builder.HasIndex(x => x.VenueName)
                   .IsUnique()
                   .HasDatabaseName("UK_VENUE_NAME");

            // 业务字段映射
            builder.Property(x => x.VenueName)
                   .HasColumnName("VENUE_NAME")
                   .HasColumnType("VARCHAR2(100 CHAR)")
                   .HasMaxLength(100)
                   .IsRequired();

            builder.Property(x => x.Address)
                   .HasColumnName("ADDRESS")
                   .HasColumnType("VARCHAR2(200 CHAR)")
                   .HasMaxLength(200)
                   .IsRequired(false);

            builder.Property(x => x.ContactPhone)
                   .HasColumnName("CONTACT_PHONE")
                   .HasColumnType("VARCHAR2(20 CHAR)")
                   .HasMaxLength(20)
                   .IsRequired(false);

            builder.Property(x => x.Status)
                   .HasColumnName("STATUS")
                   .HasColumnType("VARCHAR2(20 CHAR)")
                   .HasMaxLength(20)
                   .HasDefaultValue("ENABLED")
                   .IsRequired();

            builder.Property(x => x.Remark)
                   .HasColumnName("REMARK")
                   .HasColumnType("VARCHAR2(255 CHAR)")
                   .HasMaxLength(255)
                   .IsRequired(false);

            //复用 4 个标准审计字段（CREATE_TIME, UPDATE_TIME, CREATE_BY, UPDATE_BY）
            builder.ConfigureAuditableEntity();
        }
    }
}
