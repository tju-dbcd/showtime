using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShowtimeBackend.Data.Configurations.UserPermission;
using ShowtimeBackend.Entities.OrderTicket;

namespace ShowtimeBackend.Data.Configurations.OrderTicket;

public class RefundRequestConfiguration : IEntityTypeConfiguration<RefundRequest>
{
    public void Configure(EntityTypeBuilder<RefundRequest> builder)
    {
        builder.ToTable("REFUND_REQUEST", table =>
        {
            table.HasCheckConstraint("CHK_REFUND_TYPE", "REFUND_TYPE IN ('FULL', 'PART')");
            table.HasCheckConstraint("CHK_REFUND_AMOUNT", "REFUND_AMOUNT >= 0");
            table.HasCheckConstraint("CHK_REFUND_RATE", "FEE_RATE BETWEEN 0 AND 1");
            table.HasCheckConstraint(
                "CHK_APPROVE_STATUS",
                "APPROVE_STATUS IN ('PENDING', 'APPROVED', 'REJECTED')");
            table.HasCheckConstraint(
                "CHK_REFUND_STATUS",
                "REFUND_STATUS IN ('PENDING', 'PROCESSING', 'COMPLETED', 'FAILED')");
            table.HasCheckConstraint(
                "CHK_REFUND_APPLIED_FEE",
                "APPLIED_SERVICE_FEE >= 0");
            table.HasCheckConstraint(
                "CHK_REFUND_ACTUAL_POSITIVE",
                "ACTUAL_REFUND IS NULL OR ACTUAL_REFUND > 0");
            table.HasCheckConstraint(
                "CHK_REFUND_STATE_COMBO",
                "(APPROVE_STATUS = 'PENDING' AND REFUND_STATUS = 'PENDING') OR " +
                "(APPROVE_STATUS = 'APPROVED' AND REFUND_STATUS IN ('PROCESSING', 'COMPLETED', 'FAILED')) OR " +
                "(APPROVE_STATUS = 'REJECTED' AND REFUND_STATUS = 'FAILED')");
        });

        builder.HasKey(entity => entity.RefundId)
            .HasName("PK_REFUND_REQUEST");

        builder.Property(entity => entity.RefundId)
            .HasColumnName("REFUND_ID")
            .HasColumnType("NUMBER(19)")
            .ValueGeneratedOnAdd();

        builder.Property(entity => entity.RefundNo)
            .HasColumnName("REFUND_NO")
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

        builder.Property(entity => entity.RefundType)
            .HasColumnName("REFUND_TYPE")
            .HasColumnType("VARCHAR2(20 CHAR)")
            .HasMaxLength(20)
            .IsUnicode(false)
            .HasDefaultValue("FULL")
            .IsRequired();

        builder.Property(entity => entity.RefundReason)
            .HasColumnName("REFUND_REASON")
            .HasColumnType("VARCHAR2(500 CHAR)")
            .HasMaxLength(500)
            .IsUnicode(false);

        builder.Property(entity => entity.RefundAmount)
            .HasColumnName("REFUND_AMOUNT")
            .HasColumnType("NUMBER(12,2)")
            .HasPrecision(12, 2)
            .HasDefaultValue(0m)
            .IsRequired();

        builder.Property(entity => entity.ActualRefund)
            .HasColumnName("ACTUAL_REFUND")
            .HasColumnType("NUMBER(12,2)")
            .HasPrecision(12, 2);

        builder.Property(entity => entity.FeeRate)
            .HasColumnName("FEE_RATE")
            .HasColumnType("NUMBER(5,4)")
            .HasPrecision(5, 4)
            .HasDefaultValue(0m)
            .IsRequired();

        builder.Property(entity => entity.AppliedPolicyId)
            .HasColumnName("APPLIED_POLICY_ID")
            .HasColumnType("NUMBER(19)");

        builder.Property(entity => entity.AppliedServiceFee)
            .HasColumnName("APPLIED_SERVICE_FEE")
            .HasColumnType("NUMBER(10,2)")
            .HasPrecision(10, 2)
            .HasDefaultValue(0m)
            .IsRequired();

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

        builder.Property(entity => entity.RefundStatus)
            .HasColumnName("REFUND_STATUS")
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

        builder.HasIndex(entity => entity.RefundNo)
            .IsUnique()
            .HasDatabaseName("UK_REFUND_NO");

        builder.HasIndex(entity => entity.OrderId)
            .HasDatabaseName("IDX_REFUND_ORDER");

        builder.HasIndex(entity => entity.UserId)
            .HasDatabaseName("IDX_REFUND_USER");

        builder.HasIndex(entity => entity.ApproveStatus)
            .HasDatabaseName("IDX_REFUND_APPROVE");

        builder.HasIndex(entity => entity.RefundStatus)
            .HasDatabaseName("IDX_REFUND_STATUS");

        builder.HasIndex(entity => entity.CompleteTime)
            .HasDatabaseName("IDX_REFUND_COMPLETE");

        builder.HasIndex(entity => entity.AppliedPolicyId)
            .HasDatabaseName("IDX_REFUND_APPLIED_POLICY");

        builder.HasIndex(entity => new { entity.OrderId, entity.RefundStatus })
            .HasDatabaseName("IDX_REFUND_ORDER_STATUS");

        builder.HasOne(entity => entity.Order)
            .WithMany(entity => entity.RefundRequests)
            .HasForeignKey(entity => entity.OrderId)
            .OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_REFUND_ORDER");

        builder.HasOne(entity => entity.User)
            .WithMany()
            .HasForeignKey(entity => entity.UserId)
            .OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_REFUND_USER");

        builder.HasOne(entity => entity.AppliedPolicy)
            .WithMany()
            .HasForeignKey(entity => entity.AppliedPolicyId)
            .OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_REFUND_APPLIED_POLICY");
    }
}
