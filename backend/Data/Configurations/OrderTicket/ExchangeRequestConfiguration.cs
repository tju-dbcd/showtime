using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShowtimeBackend.Data.Configurations.UserPermission;
using ShowtimeBackend.Entities.OrderTicket;

namespace ShowtimeBackend.Data.Configurations.OrderTicket;

public class ExchangeRequestConfiguration : IEntityTypeConfiguration<ExchangeRequest>
{
    public void Configure(EntityTypeBuilder<ExchangeRequest> builder)
    {
        builder.ToTable("EXCHANGE_REQUEST", table =>
        {
            table.HasCheckConstraint("CHK_EXCHANGE_FEE", "EXCHANGE_FEE >= 0");
            table.HasCheckConstraint("CHK_PRICE_DIFF", "PRICE_DIFF >= 0");
            table.HasCheckConstraint(
                "CHK_EXCHANGE_APPROVE",
                "APPROVE_STATUS IN ('PENDING', 'APPROVED', 'REJECTED')");
            table.HasCheckConstraint(
                "CHK_EXCHANGE_STATUS",
                "EXCHANGE_STATUS IN ('PENDING', 'PROCESSING', 'COMPLETED', 'FAILED')");
            table.HasCheckConstraint(
                "CHK_EXCHANGE_STATE_COMBO",
                "(APPROVE_STATUS = 'PENDING' AND EXCHANGE_STATUS = 'PENDING') OR " +
                "(APPROVE_STATUS = 'APPROVED' AND EXCHANGE_STATUS IN ('PROCESSING', 'COMPLETED', 'FAILED')) OR " +
                "(APPROVE_STATUS = 'REJECTED' AND EXCHANGE_STATUS = 'FAILED')");
        });

        builder.HasKey(entity => entity.ExchangeId)
            .HasName("PK_EXCHANGE_REQUEST");

        builder.Property(entity => entity.ExchangeId)
            .HasColumnName("EXCHANGE_ID")
            .HasColumnType("NUMBER(19)")
            .ValueGeneratedOnAdd();

        builder.Property(entity => entity.ExchangeNo)
            .HasColumnName("EXCHANGE_NO")
            .HasColumnType("VARCHAR2(30 CHAR)")
            .HasMaxLength(30)
            .IsUnicode(false)
            .IsRequired();

        builder.Property(entity => entity.OrderId)
            .HasColumnName("ORDER_ID")
            .HasColumnType("NUMBER(19)");

        builder.Property(entity => entity.UserId)
            .HasColumnName("USER_ID")
            .HasColumnType("NUMBER(19)");

        builder.Property(entity => entity.OrigSessionId)
            .HasColumnName("ORIG_SESSION_ID")
            .HasColumnType("NUMBER(19)");

        builder.Property(entity => entity.TargetSessionId)
            .HasColumnName("TARGET_SESSION_ID")
            .HasColumnType("NUMBER(19)");

        builder.Property(entity => entity.ExchangeReason)
            .HasColumnName("EXCHANGE_REASON")
            .HasColumnType("VARCHAR2(500 CHAR)")
            .HasMaxLength(500)
            .IsUnicode(false);

        builder.Property(entity => entity.ExchangeFee)
            .HasColumnName("EXCHANGE_FEE")
            .HasColumnType("NUMBER(10,2)")
            .HasPrecision(10, 2)
            .HasDefaultValue(0m)
            .IsRequired();

        builder.Property(entity => entity.PriceDiff)
            .HasColumnName("PRICE_DIFF")
            .HasColumnType("NUMBER(10,2)")
            .HasPrecision(10, 2)
            .HasDefaultValue(0m)
            .IsRequired();

        builder.Property(entity => entity.AppliedPolicyId)
            .HasColumnName("APPLIED_POLICY_ID")
            .HasColumnType("NUMBER(19)");

        builder.Property(entity => entity.ApproveStatus)
            .HasColumnName("APPROVE_STATUS")
            .HasColumnType("VARCHAR2(20 CHAR)")
            .HasMaxLength(20)
            .IsUnicode(false)
            .HasDefaultValue("PENDING")
            .IsConcurrencyToken()
            .IsRequired();

        builder.Property(entity => entity.ReviewBy)
            .HasColumnName("REVIEW_BY")
            .HasColumnType("VARCHAR2(50 CHAR)")
            .HasMaxLength(50)
            .IsUnicode(false);

        builder.Property(entity => entity.ReviewTime)
            .HasColumnName("REVIEW_TIME")
            .HasColumnType("TIMESTAMP(6)");

        builder.Property(entity => entity.ReviewRemark)
            .HasColumnName("REVIEW_REMARK")
            .HasColumnType("VARCHAR2(500 CHAR)")
            .HasMaxLength(500)
            .IsUnicode(false);

        builder.Property(entity => entity.ExchangeStatus)
            .HasColumnName("EXCHANGE_STATUS")
            .HasColumnType("VARCHAR2(20 CHAR)")
            .HasMaxLength(20)
            .IsUnicode(false)
            .HasDefaultValue("PENDING")
            .IsConcurrencyToken()
            .IsRequired();

        builder.Property(entity => entity.CompleteTime)
            .HasColumnName("COMPLETE_TIME")
            .HasColumnType("TIMESTAMP(6)");

        builder.ConfigureAuditableEntity();

        builder.HasIndex(entity => entity.ExchangeNo)
            .IsUnique()
            .HasDatabaseName("UK_EXCHANGE_NO");

        builder.HasIndex(entity => entity.OrderId)
            .HasDatabaseName("IDX_EXCHANGE_ORDER");

        builder.HasIndex(entity => entity.UserId)
            .HasDatabaseName("IDX_EXCHANGE_USER");

        builder.HasIndex(entity => entity.OrigSessionId)
            .HasDatabaseName("IDX_EXCHANGE_ORIG_SESSION");

        builder.HasIndex(entity => entity.TargetSessionId)
            .HasDatabaseName("IDX_EXCHANGE_TARGET_SESSION");

        builder.HasIndex(entity => entity.ExchangeStatus)
            .HasDatabaseName("IDX_EXCHANGE_STATUS");

        builder.HasIndex(entity => entity.ApproveStatus)
            .HasDatabaseName("IDX_EXCHANGE_APPROVE");

        builder.HasIndex(entity => entity.AppliedPolicyId)
            .HasDatabaseName("IDX_EXCHANGE_APPLIED_POLICY");

        builder.HasIndex(entity => new { entity.OrderId, entity.ExchangeStatus })
            .HasDatabaseName("IDX_EXCHANGE_ORDER_STATUS");

        builder.HasOne(entity => entity.Order)
            .WithMany(entity => entity.ExchangeRequests)
            .HasForeignKey(entity => entity.OrderId)
            .OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_EXCHANGE_ORDER");

        builder.HasOne(entity => entity.User)
            .WithMany()
            .HasForeignKey(entity => entity.UserId)
            .OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_EXCHANGE_USER");

        builder.HasOne(entity => entity.AppliedPolicy)
            .WithMany()
            .HasForeignKey(entity => entity.AppliedPolicyId)
            .OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_EXCHANGE_APPLIED_POLICY");
    }
}
