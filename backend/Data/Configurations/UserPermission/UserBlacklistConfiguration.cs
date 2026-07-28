using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShowtimeBackend.Entities.UserPermission;

namespace ShowtimeBackend.Data.Configurations.UserPermission;

public class UserBlacklistConfiguration : IEntityTypeConfiguration<UserBlacklist>
{
    public void Configure(EntityTypeBuilder<UserBlacklist> builder)
    {
        builder.ToTable("USER_BLACKLIST", table =>
        {
            table.HasCheckConstraint(
                "CK_BLACKLIST_RISK_SCORE",
                "RISK_SCORE BETWEEN 0 AND 100");
            table.HasCheckConstraint(
                "CK_BLACKLIST_PERMANENT",
                "IS_PERMANENT IN (0, 1)");
            table.HasCheckConstraint(
                "CK_BLACKLIST_STATUS",
                "STATUS IN (0, 1)");
            table.HasCheckConstraint(
                "CK_BLACKLIST_TIME",
                "END_TIME IS NULL OR END_TIME >= START_TIME");
        });

        builder.HasKey(entity => entity.BlacklistId)
            .HasName("PK_USER_BLACKLIST");

        builder.Property(entity => entity.BlacklistId)
            .HasColumnName("BLACKLIST_ID")
            .HasColumnType("NUMBER(19)")
            .ValueGeneratedOnAdd();

        builder.Property(entity => entity.UserId)
            .HasColumnName("USER_ID")
            .HasColumnType("NUMBER(19)")
            .IsRequired();

        builder.Property(entity => entity.ShowId)
            .HasColumnName("SHOW_ID")
            .HasColumnType("NUMBER(19)");

        builder.Property(entity => entity.RiskType)
            .HasColumnName("RISK_TYPE")
            .HasColumnType("VARCHAR2(30 CHAR)")
            .HasMaxLength(30)
            .IsUnicode(false)
            .IsRequired();

        builder.Property(entity => entity.RiskScore)
            .HasColumnName("RISK_SCORE")
            .HasColumnType("NUMBER(5)")
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(entity => entity.StartTime)
            .HasColumnName("START_TIME")
            .HasColumnType("TIMESTAMP(6)")
            .HasDefaultValueSql("CURRENT_TIMESTAMP")
            .ValueGeneratedOnAdd()
            .IsRequired();

        builder.Property(entity => entity.EndTime)
            .HasColumnName("END_TIME")
            .HasColumnType("TIMESTAMP(6)");

        builder.Property(entity => entity.IsPermanent)
            .HasColumnName("IS_PERMANENT")
            .HasColumnType("NUMBER(1)")
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(entity => entity.Reason)
            .HasColumnName("REASON")
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

        builder.HasIndex(entity => entity.UserId)
            .HasDatabaseName("IDX_BLACKLIST_USER");

        builder.HasIndex(entity => entity.ShowId)
            .HasDatabaseName("IDX_BLACKLIST_SHOW");

        builder.HasIndex(entity => entity.Status)
            .HasDatabaseName("IDX_BLACKLIST_STATUS");

        builder.HasOne(entity => entity.User)
            .WithMany(entity => entity.BlacklistEntries)
            .HasForeignKey(entity => entity.UserId)
            .OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_BLACKLIST_USER");
    }
}
