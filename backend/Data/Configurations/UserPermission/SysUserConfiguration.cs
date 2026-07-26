using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShowtimeBackend.Entities.UserPermission;

namespace ShowtimeBackend.Data.Configurations.UserPermission;

public class SysUserConfiguration : IEntityTypeConfiguration<SysUser>
{
    public void Configure(EntityTypeBuilder<SysUser> builder)
    {
        builder.ToTable("SYS_USER", table =>
        {
            table.HasCheckConstraint(
                "CK_SYS_USER_TYPE",
                "USER_TYPE IN ('NORMAL', 'MEMBER', 'VIP')");
            table.HasCheckConstraint(
                "CK_SYS_USER_STATUS",
                "STATUS IN (0, 1, 2)");
        });

        builder.HasKey(entity => entity.UserId)
            .HasName("PK_SYS_USER");

        builder.Property(entity => entity.UserId)
            .HasColumnName("USER_ID")
            .HasColumnType("NUMBER(19)")
            .ValueGeneratedOnAdd();

        builder.Property(entity => entity.UserName)
            .HasColumnName("USER_NAME")
            .HasColumnType("VARCHAR2(50 CHAR)")
            .HasMaxLength(50)
            .IsUnicode(false)
            .IsRequired();

        builder.Property(entity => entity.PasswordHash)
            .HasColumnName("PASSWORD_HASH")
            .HasColumnType("VARCHAR2(255 CHAR)")
            .HasMaxLength(255)
            .IsUnicode(false)
            .IsRequired();

        builder.Property(entity => entity.Nickname)
            .HasColumnName("NICKNAME")
            .HasColumnType("VARCHAR2(50 CHAR)")
            .HasMaxLength(50)
            .IsUnicode(false);

        builder.Property(entity => entity.Phone)
            .HasColumnName("PHONE")
            .HasColumnType("VARCHAR2(20 CHAR)")
            .HasMaxLength(20)
            .IsUnicode(false)
            .IsRequired();

        builder.Property(entity => entity.Email)
            .HasColumnName("EMAIL")
            .HasColumnType("VARCHAR2(100 CHAR)")
            .HasMaxLength(100)
            .IsUnicode(false);

        builder.Property(entity => entity.OrgId)
            .HasColumnName("ORG_ID")
            .HasColumnType("NUMBER(19)");

        builder.Property(entity => entity.UserType)
            .HasColumnName("USER_TYPE")
            .HasColumnType("VARCHAR2(20 CHAR)")
            .HasMaxLength(20)
            .IsUnicode(false)
            .HasDefaultValue("NORMAL")
            .IsRequired();

        builder.Property(entity => entity.Status)
            .HasColumnName("STATUS")
            .HasColumnType("NUMBER(1)")
            .HasDefaultValue((byte)1)
            .HasSentinel(byte.MaxValue)
            .IsRequired();

        builder.ConfigureAuditableEntity();

        builder.HasIndex(entity => entity.UserName)
            .IsUnique()
            .HasDatabaseName("UK_SYS_USER_NAME");

        builder.HasIndex(entity => entity.Phone)
            .IsUnique()
            .HasDatabaseName("UK_SYS_USER_PHONE");

        builder.HasIndex(entity => entity.Email)
            .IsUnique()
            .HasDatabaseName("UK_SYS_USER_EMAIL");

        builder.HasIndex(entity => entity.OrgId)
            .HasDatabaseName("IDX_SYS_USER_ORG");

        builder.HasIndex(entity => entity.UserType)
            .HasDatabaseName("IDX_SYS_USER_TYPE");

        builder.HasOne(entity => entity.Organization)
            .WithMany(entity => entity.Users)
            .HasForeignKey(entity => entity.OrgId)
            .OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_SYS_USER_ORG");
    }
}
