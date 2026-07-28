using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShowtimeBackend.Entities.UserPermission;

namespace ShowtimeBackend.Data.Configurations.UserPermission;

public class RolePermissionConfiguration : IEntityTypeConfiguration<RolePermission>
{
    public void Configure(EntityTypeBuilder<RolePermission> builder)
    {
        builder.ToTable("ROLE_PERMISSION");

        builder.HasKey(entity => entity.RolePermId)
            .HasName("PK_ROLE_PERMISSION");

        builder.Property(entity => entity.RolePermId)
            .HasColumnName("ROLE_PERM_ID")
            .HasColumnType("NUMBER(19)")
            .ValueGeneratedOnAdd();

        builder.Property(entity => entity.RoleId)
            .HasColumnName("ROLE_ID")
            .HasColumnType("NUMBER(19)")
            .IsRequired();

        builder.Property(entity => entity.PermissionId)
            .HasColumnName("PERMISSION_ID")
            .HasColumnType("NUMBER(19)")
            .IsRequired();

        builder.HasIndex(entity => new { entity.RoleId, entity.PermissionId })
            .IsUnique()
            .HasDatabaseName("UK_ROLE_PERMISSION");

        builder.HasIndex(entity => entity.PermissionId)
            .HasDatabaseName("IDX_RP_PERMISSION");

        builder.HasOne(entity => entity.Role)
            .WithMany(entity => entity.RolePermissions)
            .HasForeignKey(entity => entity.RoleId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("FK_RP_ROLE");

        builder.HasOne(entity => entity.Permission)
            .WithMany(entity => entity.RolePermissions)
            .HasForeignKey(entity => entity.PermissionId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("FK_RP_PERMISSION");
    }
}
