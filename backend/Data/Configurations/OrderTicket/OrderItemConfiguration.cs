using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShowtimeBackend.Data.Configurations.UserPermission;
using ShowtimeBackend.Entities.OrderTicket;

namespace ShowtimeBackend.Data.Configurations.OrderTicket;

public class OrderItemConfiguration : IEntityTypeConfiguration<OrderItem>
{
    public void Configure(EntityTypeBuilder<OrderItem> builder)
    {
        builder.ToTable("ORDER_ITEM", table =>
        {
            table.HasCheckConstraint(
                "CHK_ORDER_ITEM_STATUS",
                "ITEM_STATUS IN ('NORMAL', 'REFUNDING', 'REFUNDED', 'EXCHANGING', 'EXCHANGED')");
            table.HasCheckConstraint("CHK_ORDER_ITEM_PRICE", "UNIT_PRICE >= 0");
        });

        builder.HasKey(entity => entity.OrderItemId)
            .HasName("PK_ORDER_ITEM");

        builder.Property(entity => entity.OrderItemId)
            .HasColumnName("ORDER_ITEM_ID")
            .HasColumnType("NUMBER(19)")
            .ValueGeneratedOnAdd();

        builder.Property(entity => entity.OrderId)
            .HasColumnName("ORDER_ID")
            .HasColumnType("NUMBER(19)");

        builder.Property(entity => entity.SeatId)
            .HasColumnName("SEAT_ID")
            .HasColumnType("NUMBER(19)");

        builder.Property(entity => entity.PriceStrategyId)
            .HasColumnName("PRICE_STRATEGY_ID")
            .HasColumnType("NUMBER(19)");

        builder.Property(entity => entity.RealNameId)
            .HasColumnName("REAL_NAME_ID")
            .HasColumnType("NUMBER(19)");

        builder.Property(entity => entity.UnitPrice)
            .HasColumnName("UNIT_PRICE")
            .HasColumnType("NUMBER(10,2)")
            .HasPrecision(10, 2)
            .HasDefaultValue(0m)
            .IsRequired();

        builder.Property(entity => entity.ItemStatus)
            .HasColumnName("ITEM_STATUS")
            .HasColumnType("VARCHAR2(20 CHAR)")
            .HasMaxLength(20)
            .IsUnicode(false)
            .HasDefaultValue("NORMAL")
            .IsConcurrencyToken()
            .IsRequired();

        builder.ConfigureAuditableEntity();

        builder.HasIndex(entity => entity.OrderId)
            .HasDatabaseName("IDX_ORDER_ITEM_ORDER");

        builder.HasIndex(entity => entity.SeatId)
            .HasDatabaseName("IDX_ORDER_ITEM_SEAT");

        builder.HasIndex(entity => entity.PriceStrategyId)
            .HasDatabaseName("IDX_ORDER_ITEM_PRICE");

        builder.HasIndex(entity => entity.RealNameId)
            .HasDatabaseName("IDX_ORDER_ITEM_REALNAME");

        builder.HasIndex(entity => entity.ItemStatus)
            .HasDatabaseName("IDX_ORDER_ITEM_STATUS");

        builder.HasIndex(entity => new { entity.OrderId, entity.ItemStatus })
            .HasDatabaseName("IDX_ORDER_ITEM_ORDER_STATUS");

        builder.HasOne(entity => entity.Order)
            .WithMany(entity => entity.Items)
            .HasForeignKey(entity => entity.OrderId)
            .OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_ORDER_ITEM_ORDER");

        builder.HasOne(entity => entity.RealName)
            .WithMany()
            .HasForeignKey(entity => entity.RealNameId)
            .OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_ORDER_ITEM_REALNAME");
    }
}
