using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShowtimeBackend.Data.Configurations.UserPermission;
using ShowtimeBackend.Entities.OrderTicket;

namespace ShowtimeBackend.Data.Configurations.OrderTicket;

public class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("PAYMENT", table =>
        {
            table.HasCheckConstraint("CHK_PAYMENT_AMOUNT", "PAY_AMOUNT >= 0");
            table.HasCheckConstraint(
                "CHK_PAYMENT_REFUND",
                "REFUND_AMOUNT >= 0 AND REFUND_AMOUNT <= PAY_AMOUNT");
            table.HasCheckConstraint(
                "CHK_PAYMENT_CHANNEL",
                "PAY_CHANNEL IN ('ALIPAY', 'WECHAT', 'UNIONPAY', 'BALANCE')");
            table.HasCheckConstraint(
                "CHK_PAYMENT_STATUS",
                "PAY_STATUS IN ('PENDING', 'SUCCESS', 'FAIL', 'CLOSED')");
        });

        builder.HasKey(entity => entity.PaymentId)
            .HasName("PK_PAYMENT");

        builder.Property(entity => entity.PaymentId)
            .HasColumnName("PAYMENT_ID")
            .HasColumnType("NUMBER(19)")
            .ValueGeneratedOnAdd();

        builder.Property(entity => entity.PaymentNo)
            .HasColumnName("PAYMENT_NO")
            .HasColumnType("VARCHAR2(50 CHAR)")
            .HasMaxLength(50)
            .IsUnicode(false)
            .IsRequired();

        builder.Property(entity => entity.OrderId)
            .HasColumnName("ORDER_ID")
            .HasColumnType("NUMBER(19)");

        builder.Property(entity => entity.UserId)
            .HasColumnName("USER_ID")
            .HasColumnType("NUMBER(19)");

        builder.Property(entity => entity.PayAmount)
            .HasColumnName("PAY_AMOUNT")
            .HasColumnType("NUMBER(12,2)")
            .HasPrecision(12, 2)
            .HasDefaultValue(0m)
            .IsRequired();

        builder.Property(entity => entity.PayChannel)
            .HasColumnName("PAY_CHANNEL")
            .HasColumnType("VARCHAR2(20 CHAR)")
            .HasMaxLength(20)
            .IsUnicode(false)
            .IsRequired();

        builder.Property(entity => entity.PayStatus)
            .HasColumnName("PAY_STATUS")
            .HasColumnType("VARCHAR2(20 CHAR)")
            .HasMaxLength(20)
            .IsUnicode(false)
            .HasDefaultValue("PENDING")
            .IsRequired();

        builder.Property(entity => entity.TradeNo)
            .HasColumnName("TRADE_NO")
            .HasColumnType("VARCHAR2(100 CHAR)")
            .HasMaxLength(100)
            .IsUnicode(false);

        builder.Property(entity => entity.CallbackData)
            .HasColumnName("CALLBACK_DATA")
            .HasColumnType("CLOB");

        builder.Property(entity => entity.CallbackTime)
            .HasColumnName("CALLBACK_TIME")
            .HasColumnType("TIMESTAMP(6)");

        builder.Property(entity => entity.PayTime)
            .HasColumnName("PAY_TIME")
            .HasColumnType("TIMESTAMP(6)");

        builder.Property(entity => entity.RefundAmount)
            .HasColumnName("REFUND_AMOUNT")
            .HasColumnType("NUMBER(12,2)")
            .HasPrecision(12, 2)
            .HasDefaultValue(0m)
            .IsRequired();

        builder.ConfigureAuditableEntity();

        builder.HasIndex(entity => entity.PaymentNo)
            .IsUnique()
            .HasDatabaseName("UK_PAYMENT_NO");

        builder.HasIndex(entity => entity.OrderId)
            .HasDatabaseName("IDX_PAYMENT_ORDER");

        builder.HasIndex(entity => entity.UserId)
            .HasDatabaseName("IDX_PAYMENT_USER");

        builder.HasIndex(entity => entity.PayStatus)
            .HasDatabaseName("IDX_PAYMENT_STATUS");

        builder.HasIndex(entity => entity.PayTime)
            .HasDatabaseName("IDX_PAYMENT_PAYTIME");

        builder.HasIndex(entity => new { entity.OrderId, entity.PayStatus })
            .HasDatabaseName("IDX_PAYMENT_ORDER_STATUS");

        builder.HasOne(entity => entity.Order)
            .WithMany(entity => entity.Payments)
            .HasForeignKey(entity => entity.OrderId)
            .OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_PAYMENT_ORDER");

        builder.HasOne(entity => entity.User)
            .WithMany()
            .HasForeignKey(entity => entity.UserId)
            .OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_PAYMENT_USER");
    }
}
