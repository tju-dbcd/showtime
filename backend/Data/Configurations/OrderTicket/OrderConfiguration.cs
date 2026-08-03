using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShowtimeBackend.Data.Configurations.UserPermission;
using ShowtimeBackend.Entities.OrderTicket;

namespace ShowtimeBackend.Data.Configurations.OrderTicket;

public class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("T_ORDER", table =>
        {
            table.HasCheckConstraint(
                "CHK_T_ORDER_TYPE",
                "ORDER_TYPE IN ('NORMAL', 'SPLIT', 'MERGE', 'EXCHANGE')");
            table.HasCheckConstraint(
                "CHK_T_ORDER_STATUS",
                "ORDER_STATUS IN ('PENDING_PAY', 'PAID', 'ISSUED', 'PART_REFUND', 'REFUNDED', 'CANCELLED')");
            table.HasCheckConstraint("CHK_T_ORDER_TOTAL", "TOTAL_AMOUNT >= 0");
            table.HasCheckConstraint("CHK_T_ORDER_DISCOUNT", "DISCOUNT_AMOUNT >= 0");
            table.HasCheckConstraint("CHK_T_ORDER_COUNT", "TICKET_COUNT > 0");
        });

        builder.HasKey(entity => entity.OrderId)
            .HasName("PK_T_ORDER");

        builder.Property(entity => entity.OrderId)
            .HasColumnName("ORDER_ID")
            .HasColumnType("NUMBER(19)")
            .ValueGeneratedOnAdd();

        builder.Property(entity => entity.OrderNo)
            .HasColumnName("ORDER_NO")
            .HasColumnType("VARCHAR2(30 CHAR)")
            .HasMaxLength(30)
            .IsUnicode(false)
            .IsRequired();

        builder.Property(entity => entity.UserId)
            .HasColumnName("USER_ID")
            .HasColumnType("NUMBER(19)");

        builder.Property(entity => entity.SessionId)
            .HasColumnName("SESSION_ID")
            .HasColumnType("NUMBER(19)");

        builder.Property(entity => entity.OrderType)
            .HasColumnName("ORDER_TYPE")
            .HasColumnType("VARCHAR2(20 CHAR)")
            .HasMaxLength(20)
            .IsUnicode(false)
            .HasDefaultValue("NORMAL")
            .IsRequired();

        builder.Property(entity => entity.ParentOrderId)
            .HasColumnName("PARENT_ORDER_ID")
            .HasColumnType("NUMBER(19)");

        builder.Property(entity => entity.TotalAmount)
            .HasColumnName("TOTAL_AMOUNT")
            .HasColumnType("NUMBER(12,2)")
            .HasPrecision(12, 2)
            .HasDefaultValue(0m)
            .IsRequired();

        builder.Property(entity => entity.DiscountAmount)
            .HasColumnName("DISCOUNT_AMOUNT")
            .HasColumnType("NUMBER(12,2)")
            .HasPrecision(12, 2)
            .HasDefaultValue(0m)
            .IsRequired();

        builder.Property(entity => entity.TicketCount)
            .HasColumnName("TICKET_COUNT")
            .HasColumnType("NUMBER(5)")
            .HasDefaultValue(1)
            .IsRequired();

        builder.Property(entity => entity.OrderStatus)
            .HasColumnName("ORDER_STATUS")
            .HasColumnType("VARCHAR2(20 CHAR)")
            .HasMaxLength(20)
            .IsUnicode(false)
            .HasDefaultValue("PENDING_PAY")
            .IsRequired();

        builder.Property(entity => entity.ExpireTime)
            .HasColumnName("EXPIRE_TIME")
            .HasColumnType("TIMESTAMP(6)")
            .HasDefaultValueSql("CURRENT_TIMESTAMP")
            .IsRequired();

        builder.Property(entity => entity.PayTime)
            .HasColumnName("PAY_TIME")
            .HasColumnType("TIMESTAMP(6)");

        builder.Property(entity => entity.IssueTime)
            .HasColumnName("ISSUE_TIME")
            .HasColumnType("TIMESTAMP(6)");

        builder.Property(entity => entity.CancelTime)
            .HasColumnName("CANCEL_TIME")
            .HasColumnType("TIMESTAMP(6)");

        builder.Property(entity => entity.Source)
            .HasColumnName("SOURCE")
            .HasColumnType("VARCHAR2(20 CHAR)")
            .HasMaxLength(20)
            .IsUnicode(false)
            .HasDefaultValue("WEB")
            .IsRequired();

        builder.Property(entity => entity.IpAddress)
            .HasColumnName("IP_ADDRESS")
            .HasColumnType("VARCHAR2(50 CHAR)")
            .HasMaxLength(50)
            .IsUnicode(false);

        builder.Property(entity => entity.Remark)
            .HasColumnName("REMARK")
            .HasColumnType("VARCHAR2(500 CHAR)")
            .HasMaxLength(500)
            .IsUnicode(false);

        builder.ConfigureAuditableEntity();

        builder.HasIndex(entity => entity.OrderNo)
            .IsUnique()
            .HasDatabaseName("UK_T_ORDER_NO");

        builder.HasIndex(entity => entity.UserId)
            .HasDatabaseName("IDX_T_ORDER_USER");

        builder.HasIndex(entity => entity.SessionId)
            .HasDatabaseName("IDX_T_ORDER_SESSION");

        builder.HasIndex(entity => entity.OrderStatus)
            .HasDatabaseName("IDX_T_ORDER_STATUS");

        builder.HasIndex(entity => entity.CreateTime)
            .HasDatabaseName("IDX_T_ORDER_CREATE_TIME");

        builder.HasIndex(entity => new { entity.UserId, entity.OrderStatus })
            .HasDatabaseName("IDX_T_ORDER_USER_STATUS");

        builder.HasOne(entity => entity.User)
            .WithMany()
            .HasForeignKey(entity => entity.UserId)
            .OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_T_ORDER_USER");

        builder.HasOne(entity => entity.ParentOrder)
            .WithMany(entity => entity.ChildOrders)
            .HasForeignKey(entity => entity.ParentOrderId)
            .OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_T_ORDER_PARENT");
    }
}
