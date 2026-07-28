using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShowtimeBackend.Entities.UserPermission;

namespace ShowtimeBackend.Data.Configurations.UserPermission;

public class PermissionConfiguration : IEntityTypeConfiguration<Permission>
{
    public void Configure(EntityTypeBuilder<Permission> builder)
    {
        builder.ToTable("PERMISSION", table =>
        {
            table.HasCheckConstraint(
                "CK_PERMISSION_TYPE",
                "RESOURCE_TYPE IN ('MENU', 'BUTTON', 'API', 'DATA')");
            table.HasCheckConstraint(
                "CK_PERMISSION_STATUS",
                "STATUS IN (0, 1)");
        });

        builder.HasKey(entity => entity.PermissionId)
            .HasName("PK_PERMISSION");

        builder.Property(entity => entity.PermissionId)
            .HasColumnName("PERMISSION_ID")
            .HasColumnType("NUMBER(19)")
            .ValueGeneratedOnAdd();

        builder.Property(entity => entity.PermCode)
            .HasColumnName("PERM_CODE")
            .HasColumnType("VARCHAR2(100 CHAR)")
            .HasMaxLength(100)
            .IsUnicode(false)
            .IsRequired();

        builder.Property(entity => entity.PermName)
            .HasColumnName("PERM_NAME")
            .HasColumnType("VARCHAR2(50 CHAR)")
            .HasMaxLength(50)
            .IsUnicode(false)
            .IsRequired();

        builder.Property(entity => entity.ResourceType)
            .HasColumnName("RESOURCE_TYPE")
            .HasColumnType("VARCHAR2(20 CHAR)")
            .HasMaxLength(20)
            .IsUnicode(false)
            .IsRequired();

        builder.Property(entity => entity.ParentId)
            .HasColumnName("PARENT_ID")
            .HasColumnType("NUMBER(19)");

        builder.Property(entity => entity.SortOrder)
            .HasColumnName("SORT_ORDER")
            .HasColumnType("NUMBER(5)")
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(entity => entity.Status)
            .HasColumnName("STATUS")
            .HasColumnType("NUMBER(1)")
            .HasDefaultValue(true)
            .HasSentinel(true)
            .IsRequired();

        builder.ConfigureAuditableEntity();

        builder.HasIndex(entity => entity.PermCode)
            .IsUnique()
            .HasDatabaseName("UK_PERMISSION_CODE");

        builder.HasIndex(entity => entity.ParentId)
            .HasDatabaseName("IDX_PERMISSION_PARENT");

        builder.HasOne(entity => entity.Parent)
            .WithMany(entity => entity.Children)
            .HasForeignKey(entity => entity.ParentId)
            .OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_PERMISSION_PARENT");
    }
}
