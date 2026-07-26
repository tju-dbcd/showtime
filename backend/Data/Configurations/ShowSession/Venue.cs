using ShowtimeBackend.Entities.ShowSessions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ShowtimeBackend.Data.Configurations.ShowSessions
{
    public class VenueConfiguration : IEntityTypeConfiguration<Venue>
    {
        public void Configure(EntityTypeBuilder<Venue> builder)
        {
            // 映射表名
            builder.ToTable("VENUE");

            // 主键配置
            builder.HasKey(v => v.VenueId);
            builder.Property(v => v.VenueId)
                   .HasColumnName("VENUE_ID")
                   .HasColumnType("NUMBER(19)")
                   .ValueGeneratedOnAdd(); // 自增主键

            // 业务属性与字段限制
            builder.Property(v => v.VenueName)
                   .HasColumnName("VENUE_NAME")
                   .HasColumnType("VARCHAR2(100 CHAR)")
                   .HasMaxLength(100)
                   .IsRequired();

            builder.Property(v => v.Address)
                   .HasColumnName("ADDRESS")
                   .HasColumnType("VARCHAR2(200 CHAR)")
                   .HasMaxLength(200)
                   .IsRequired(false);

            builder.Property(v => v.ContactPhone)
                   .HasColumnName("CONTACT_PHONE")
                   .HasColumnType("VARCHAR2(20 CHAR)")
                   .HasMaxLength(20)
                   .IsRequired(false);

            builder.Property(v => v.Status)
                   .HasColumnName("STATUS")
                   .HasColumnType("VARCHAR2(20 CHAR)")
                   .HasMaxLength(20)
                   .HasDefaultValue("ENABLED")
                   .IsRequired();

            builder.Property(v => v.Remark)
                   .HasColumnName("REMARK")
                   .HasColumnType("VARCHAR2(255 CHAR)")
                   .HasMaxLength(255)
                   .IsRequired(false);

            // 审计字段配置
            builder.Property(v => v.CreateTime)
                   .HasColumnName("CREATE_TIME")
                   .HasColumnType("TIMESTAMP(6)")
                   .HasDefaultValueSql("CURRENT_TIMESTAMP")
                   .IsRequired();

            builder.Property(v => v.UpdateTime)
                   .HasColumnName("UPDATE_TIME")
                   .HasColumnType("TIMESTAMP(6)")
                   .HasDefaultValueSql("CURRENT_TIMESTAMP")
                   .IsRequired();

            builder.Property(v => v.CreateBy)
                   .HasColumnName("CREATE_BY")
                   .HasColumnType("VARCHAR2(50 CHAR)")
                   .HasMaxLength(50)
                   .IsRequired(false);

            builder.Property(v => v.UpdateBy)
                   .HasColumnName("UPDATE_BY")
                   .HasColumnType("VARCHAR2(50 CHAR)")
                   .HasMaxLength(50)
                   .IsRequired(false);
            builder.ConfigureAuditableEntity();
        }
    }
}
