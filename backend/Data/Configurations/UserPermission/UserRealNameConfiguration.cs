using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShowtimeBackend.Entities.UserPermission;

namespace ShowtimeBackend.Data.Configurations.UserPermission;

public class UserRealNameConfiguration : IEntityTypeConfiguration<UserRealName>
{
    public void Configure(EntityTypeBuilder<UserRealName> builder)
    {
        builder.ToTable("USER_REAL_NAME", table =>
        {
            table.HasCheckConstraint(
                "CK_REAL_NAME_DEFAULT",
                "IS_DEFAULT IN (0, 1)");
            table.HasCheckConstraint(
                "CK_REAL_NAME_VERIFIED",
                "IS_VERIFIED IN (0, 1)");
        });

        builder.HasKey(entity => entity.RealNameId)
            .HasName("PK_USER_REAL_NAME");

        builder.Property(entity => entity.RealNameId)
            .HasColumnName("REAL_NAME_ID")
            .HasColumnType("NUMBER(19)")
            .ValueGeneratedOnAdd();

        builder.Property(entity => entity.UserId)
            .HasColumnName("USER_ID")
            .HasColumnType("NUMBER(19)")
            .IsRequired();

        builder.Property(entity => entity.RealName)
            .HasColumnName("REAL_NAME")
            .HasColumnType("VARCHAR2(50 CHAR)")
            .HasMaxLength(50)
            .IsUnicode(false)
            .IsRequired();

        builder.Property(entity => entity.IdCardNo)
            .HasColumnName("ID_CARD_NO")
            .HasColumnType("VARCHAR2(255 CHAR)")
            .HasMaxLength(255)
            .IsUnicode(false)
            .IsRequired();

        builder.Property(entity => entity.IsDefault)
            .HasColumnName("IS_DEFAULT")
            .HasColumnType("NUMBER(1)")
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(entity => entity.IsVerified)
            .HasColumnName("IS_VERIFIED")
            .HasColumnType("NUMBER(1)")
            .HasDefaultValue(false)
            .IsRequired();

        builder.ConfigureAuditableEntity();

        builder.HasIndex(entity => entity.UserId)
            .HasDatabaseName("IDX_REAL_NAME_USER");

        // UK_REAL_NAME_DEFAULT is an Oracle function-based index:
        // CASE WHEN IS_DEFAULT = 1 THEN USER_ID END.
        // It remains managed by the existing database because a normal
        // (UserId, IsDefault) unique index would have different semantics.

        builder.HasOne(entity => entity.User)
            .WithMany(entity => entity.RealNames)
            .HasForeignKey(entity => entity.UserId)
            .OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_REAL_NAME_USER");
    }
}
