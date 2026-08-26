using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShowtimeBackend.Data.Configurations.UserPermission;
using ShowtimeBackend.Entities.OrderTicket;

namespace ShowtimeBackend.Data.Configurations.OrderTicket;

public class RefundItemConfiguration : IEntityTypeConfiguration<RefundItem>
{
    public void Configure(EntityTypeBuilder<RefundItem> builder)
    {
        builder.ToTable("REFUND_ITEM", table =>
        {
            table.HasCheckConstraint("CHK_REFUND_BASE_AMOUNT", "REFUND_BASE_AMOUNT >= 0");
        });

        builder.HasKey(entity => entity.RefundItemId)
            .HasName("PK_REFUND_ITEM");

        builder.Property(entity => entity.RefundItemId)
            .HasColumnName("REFUND_ITEM_ID")
            .HasColumnType("NUMBER(19)")
            .ValueGeneratedOnAdd();

        builder.Property(entity => entity.RefundId)
            .HasColumnName("REFUND_ID")
            .HasColumnType("NUMBER(19)");

        builder.Property(entity => entity.OrderItemId)
            .HasColumnName("ORDER_ITEM_ID")
            .HasColumnType("NUMBER(19)");

        builder.Property(entity => entity.RefundBaseAmount)
            .HasColumnName("REFUND_BASE_AMOUNT")
            .HasColumnType("NUMBER(12,2)")
            .HasPrecision(12, 2)
            .HasDefaultValue(0m)
            .IsRequired();

        builder.ConfigureAuditableEntity();

        builder.HasIndex(entity => entity.OrderItemId)
            .IsUnique()
            .HasDatabaseName("UK_REFUND_ORDER_ITEM");

        builder.HasIndex(entity => entity.RefundId)
            .HasDatabaseName("IDX_REFUND_ITEM_REFUND");

        builder.HasOne(entity => entity.RefundRequest)
            .WithMany(entity => entity.Items)
            .HasForeignKey(entity => entity.RefundId)
            .OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_REFUND_ITEM_REQUEST");

        builder.HasOne(entity => entity.OrderItem)
            .WithOne(entity => entity.RefundItem)
            .HasForeignKey<RefundItem>(entity => entity.OrderItemId)
            .OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_REFUND_ITEM_ORDER");
    }
}
