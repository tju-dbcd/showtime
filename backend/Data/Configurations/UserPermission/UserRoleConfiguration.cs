using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShowtimeBackend.Entities.UserPermission;

namespace ShowtimeBackend.Data.Configurations.UserPermission;

public class UserRoleConfiguration : IEntityTypeConfiguration<UserRole>
{
    public void Configure(EntityTypeBuilder<UserRole> builder)
    {
        builder.ToTable("USER_ROLE");

        builder.HasKey(entity => entity.UserRoleId)
            .HasName("PK_USER_ROLE");

        builder.Property(entity => entity.UserRoleId)
            .HasColumnName("USER_ROLE_ID")
            .HasColumnType("NUMBER(19)")
            .ValueGeneratedOnAdd();

        builder.Property(entity => entity.UserId)
            .HasColumnName("USER_ID")
            .HasColumnType("NUMBER(19)")
            .IsRequired();

        builder.Property(entity => entity.RoleId)
            .HasColumnName("ROLE_ID")
            .HasColumnType("NUMBER(19)")
            .IsRequired();

        builder.HasIndex(entity => new { entity.UserId, entity.RoleId })
            .IsUnique()
            .HasDatabaseName("UK_USER_ROLE");

        builder.HasIndex(entity => entity.RoleId)
            .HasDatabaseName("IDX_USER_ROLE_ROLE");

        builder.HasOne(entity => entity.User)
            .WithMany(entity => entity.UserRoles)
            .HasForeignKey(entity => entity.UserId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("FK_USER_ROLE_USER");

        builder.HasOne(entity => entity.Role)
            .WithMany(entity => entity.UserRoles)
            .HasForeignKey(entity => entity.RoleId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("FK_USER_ROLE_ROLE");
    }
}
