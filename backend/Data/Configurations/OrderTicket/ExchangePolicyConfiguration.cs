using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShowtimeBackend.Data.Configurations.UserPermission;
using ShowtimeBackend.Entities.OrderTicket;

namespace ShowtimeBackend.Data.Configurations.OrderTicket;

public class ExchangePolicyConfiguration : IEntityTypeConfiguration<ExchangePolicy>
{
    public void Configure(EntityTypeBuilder<ExchangePolicy> builder)
    {
        builder.ToTable("EXCHANGE_POLICY", table =>
        {
            table.HasCheckConstraint(
                "CHK_EXCHANGE_POLICY_FEE",
                "EXCHANGE_FEE >= 0");
            table.HasCheckConstraint(
                "CHK_EXCHANGE_POLICY_PRIORITY",
                "PRIORITY > 0");
            table.HasCheckConstraint(
                "CHK_EXCHANGE_POLICY_STATUS",
                "STATUS IN (0, 1)");
            table.HasCheckConstraint(
                "CHK_EXCHANGE_POLICY_ALLOW",
                "ALLOW_CROSS_SESSION IN (0, 1)");
        });

        builder.HasKey(entity => entity.PolicyId)
            .HasName("PK_EXCHANGE_POLICY");

        builder.Property(entity => entity.PolicyId)
            .HasColumnName("POLICY_ID")
            .HasColumnType("NUMBER(19)")
            .ValueGeneratedOnAdd();

        builder.Property(entity => entity.ShowId)
            .HasColumnName("SHOW_ID")
            .HasColumnType("NUMBER(19)");

        builder.Property(entity => entity.PolicyName)
            .HasColumnName("POLICY_NAME")
            .HasColumnType("VARCHAR2(100 CHAR)")
            .HasMaxLength(100)
            .IsUnicode(false)
            .IsRequired();

        builder.Property(entity => entity.ExchangeDeadlineHour)
            .HasColumnName("EXCHANGE_DEADLINE_HOUR")
            .HasColumnType("NUMBER(5)")
            .IsRequired();

        builder.Property(entity => entity.ExchangeFee)
            .HasColumnName("EXCHANGE_FEE")
            .HasColumnType("NUMBER(10,2)")
            .HasPrecision(10, 2)
            .HasDefaultValue(0m)
            .IsRequired();

        builder.Property(entity => entity.AllowCrossSession)
            .HasColumnName("ALLOW_CROSS_SESSION")
            .HasColumnType("NUMBER(1)")
            .HasDefaultValue((byte)1)
            .HasSentinel(byte.MaxValue)
            .IsRequired();

        builder.Property(entity => entity.Priority)
            .HasColumnName("PRIORITY")
            .HasColumnType("NUMBER(5)")
            .HasDefaultValue(1)
            .IsRequired();

        builder.Property(entity => entity.Status)
            .HasColumnName("STATUS")
            .HasColumnType("NUMBER(1)")
            .HasDefaultValue((byte)1)
            .HasSentinel(byte.MaxValue)
            .IsRequired();

        builder.Property(entity => entity.Remark)
            .HasColumnName("REMARK")
            .HasColumnType("VARCHAR2(500)")
            .HasMaxLength(500)
            .IsUnicode(false);

        builder.ConfigureAuditableEntity();

        builder.HasIndex(entity => entity.ShowId)
            .HasDatabaseName("IDX_EXCHANGE_POLICY_SHOW");

        builder.HasIndex(entity => entity.Status)
            .HasDatabaseName("IDX_EXCHANGE_POLICY_STATUS");

        builder.HasIndex(entity => entity.Priority)
            .HasDatabaseName("IDX_EXCHANGE_POLICY_PRIORITY");
    }
}
