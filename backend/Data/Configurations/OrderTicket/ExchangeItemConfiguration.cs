using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShowtimeBackend.Data.Configurations.UserPermission;
using ShowtimeBackend.Entities.OrderTicket;

namespace ShowtimeBackend.Data.Configurations.OrderTicket;

public class ExchangeItemConfiguration : IEntityTypeConfiguration<ExchangeItem>
{
    public void Configure(EntityTypeBuilder<ExchangeItem> builder)
    {
        builder.ToTable("EXCHANGE_ITEM");

        builder.HasKey(entity => entity.ExchangeItemId)
            .HasName("PK_EXCHANGE_ITEM");

        builder.Property(entity => entity.ExchangeItemId)
            .HasColumnName("EXCHANGE_ITEM_ID")
            .HasColumnType("NUMBER(19)")
            .ValueGeneratedOnAdd();

        builder.Property(entity => entity.ExchangeId)
            .HasColumnName("EXCHANGE_ID")
            .HasColumnType("NUMBER(19)");

        builder.Property(entity => entity.OrderItemId)
            .HasColumnName("ORDER_ITEM_ID")
            .HasColumnType("NUMBER(19)");

        builder.Property(entity => entity.NewOrderItemId)
            .HasColumnName("NEW_ORDER_ITEM_ID")
            .HasColumnType("NUMBER(19)");

        builder.ConfigureAuditableEntity();

        builder.HasIndex(entity => entity.OrderItemId)
            .HasDatabaseName("IDX_EXCHANGE_ITEM_ORDER");

        builder.HasIndex(entity => entity.ExchangeId)
            .HasDatabaseName("IDX_EXCHANGE_ITEM_EXCHANGE");

        builder.HasIndex(entity => entity.NewOrderItemId)
            .HasDatabaseName("IDX_EXCHANGE_ITEM_NEW_ITEM");

        builder.HasOne(entity => entity.ExchangeRequest)
            .WithMany(entity => entity.Items)
            .HasForeignKey(entity => entity.ExchangeId)
            .OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_EXCHANGE_ITEM_REQUEST");

        builder.HasOne(entity => entity.OrderItem)
            .WithMany(entity => entity.OriginalExchangeItems)
            .HasForeignKey(entity => entity.OrderItemId)
            .OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_EXCHANGE_ITEM_ORDER");

        builder.HasOne(entity => entity.NewOrderItem)
            .WithMany(entity => entity.NewExchangeItems)
            .HasForeignKey(entity => entity.NewOrderItemId)
            .OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_EXCHANGE_ITEM_NEW_ORDER");
    }
}
