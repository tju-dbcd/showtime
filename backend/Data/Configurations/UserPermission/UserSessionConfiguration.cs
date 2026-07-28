using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShowtimeBackend.Entities.UserPermission;

namespace ShowtimeBackend.Data.Configurations.UserPermission;

public class UserSessionConfiguration : IEntityTypeConfiguration<UserSession>
{
    public void Configure(EntityTypeBuilder<UserSession> builder)
    {
        builder.ToTable("USER_SESSION", table =>
        {
            table.HasCheckConstraint(
                "CK_USER_SESSION_STATUS",
                "STATUS IN ('ACTIVE', 'EXPIRED', 'LOGOUT', 'LOCKED')");
            table.HasCheckConstraint(
                "CK_USER_SESSION_RISK",
                "RISK_FLAG IN (0, 1)");
        });

        builder.HasKey(entity => entity.UserSessionId)
            .HasName("PK_USER_SESSION");

        builder.Property(entity => entity.UserSessionId)
            .HasColumnName("USER_SESSION_ID")
            .HasColumnType("NUMBER(19)")
            .ValueGeneratedOnAdd();

        builder.Property(entity => entity.UserId)
            .HasColumnName("USER_ID")
            .HasColumnType("NUMBER(19)")
            .IsRequired();

        builder.Property(entity => entity.SessionToken)
            .HasColumnName("SESSION_TOKEN")
            .HasColumnType("VARCHAR2(128 CHAR)")
            .HasMaxLength(128)
            .IsUnicode(false)
            .IsRequired();

        builder.Property(entity => entity.LoginTime)
            .HasColumnName("LOGIN_TIME")
            .HasColumnType("TIMESTAMP(6)")
            .HasDefaultValueSql("CURRENT_TIMESTAMP")
            .ValueGeneratedOnAdd()
            .IsRequired();

        builder.Property(entity => entity.ExpireTime)
            .HasColumnName("EXPIRE_TIME")
            .HasColumnType("TIMESTAMP(6)")
            .IsRequired();

        builder.Property(entity => entity.LogoutTime)
            .HasColumnName("LOGOUT_TIME")
            .HasColumnType("TIMESTAMP(6)");

        builder.Property(entity => entity.IpAddress)
            .HasColumnName("IP_ADDRESS")
            .HasColumnType("VARCHAR2(50 CHAR)")
            .HasMaxLength(50)
            .IsUnicode(false);

        builder.Property(entity => entity.UserAgent)
            .HasColumnName("USER_AGENT")
            .HasColumnType("VARCHAR2(500 CHAR)")
            .HasMaxLength(500)
            .IsUnicode(false);

        builder.Property(entity => entity.RiskFlag)
            .HasColumnName("RISK_FLAG")
            .HasColumnType("NUMBER(1)")
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(entity => entity.Status)
            .HasColumnName("STATUS")
            .HasColumnType("VARCHAR2(20 CHAR)")
            .HasMaxLength(20)
            .IsUnicode(false)
            .HasDefaultValue("ACTIVE")
            .IsRequired();

        builder.ConfigureAuditableEntity();

        builder.HasIndex(entity => entity.SessionToken)
            .IsUnique()
            .HasDatabaseName("UK_USER_SESSION_TOKEN");

        builder.HasIndex(entity => entity.UserId)
            .HasDatabaseName("IDX_USER_SESSION_USER");

        builder.HasIndex(entity => entity.ExpireTime)
            .HasDatabaseName("IDX_USER_SESSION_EXPIRE");

        builder.HasIndex(entity => entity.Status)
            .HasDatabaseName("IDX_USER_SESSION_STATUS");

        builder.HasOne(entity => entity.User)
            .WithMany(entity => entity.Sessions)
            .HasForeignKey(entity => entity.UserId)
            .OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_USER_SESSION_USER");
    }
}
