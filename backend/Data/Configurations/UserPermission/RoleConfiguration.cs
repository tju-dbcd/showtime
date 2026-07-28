using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShowtimeBackend.Entities.UserPermission;

namespace ShowtimeBackend.Data.Configurations.UserPermission;

public class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("ROLE", table =>
        {
            table.HasCheckConstraint(
                "CK_ROLE_STATUS",
                "STATUS IN (0, 1)");
        });

        builder.HasKey(entity => entity.RoleId)
            .HasName("PK_ROLE");

        builder.Property(entity => entity.RoleId)
            .HasColumnName("ROLE_ID")
            .HasColumnType("NUMBER(19)")
            .ValueGeneratedOnAdd();

        builder.Property(entity => entity.RoleCode)
            .HasColumnName("ROLE_CODE")
            .HasColumnType("VARCHAR2(50 CHAR)")
            .HasMaxLength(50)
            .IsUnicode(false)
            .IsRequired();

        builder.Property(entity => entity.RoleName)
            .HasColumnName("ROLE_NAME")
            .HasColumnType("VARCHAR2(50 CHAR)")
            .HasMaxLength(50)
            .IsUnicode(false)
            .IsRequired();

        builder.Property(entity => entity.RoleDesc)
            .HasColumnName("ROLE_DESC")
            .HasColumnType("VARCHAR2(200 CHAR)")
            .HasMaxLength(200)
            .IsUnicode(false);

        builder.Property(entity => entity.Status)
            .HasColumnName("STATUS")
            .HasColumnType("NUMBER(1)")
            .HasDefaultValue(true)
            .HasSentinel(true)
            .IsRequired();

        builder.ConfigureAuditableEntity();

        builder.HasIndex(entity => entity.RoleCode)
            .IsUnique()
            .HasDatabaseName("UK_ROLE_CODE");

        builder.HasIndex(entity => entity.RoleName)
            .IsUnique()
            .HasDatabaseName("UK_ROLE_NAME");
    }
}
