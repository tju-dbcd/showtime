using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShowtimeBackend.Data.Configurations.UserPermission;
using ShowtimeBackend.Entities.OrderTicket;

namespace ShowtimeBackend.Data.Configurations.OrderTicket;

public class ETicketConfiguration : IEntityTypeConfiguration<ETicket>
{
    public void Configure(EntityTypeBuilder<ETicket> builder)
    {
        builder.ToTable("E_TICKET", table =>
        {
            table.HasCheckConstraint(
                "CHK_ETICKET_STATUS",
                "TICKET_STATUS IN ('UNUSED', 'USED', 'REFUNDED', 'EXCHANGED')");
        });

        builder.HasKey(entity => entity.ETicketId)
            .HasName("PK_E_TICKET");

        builder.Property(entity => entity.ETicketId)
            .HasColumnName("ETICKET_ID")
            .HasColumnType("NUMBER(19)")
            .ValueGeneratedOnAdd();

        builder.Property(entity => entity.ETicketNo)
            .HasColumnName("ETICKET_NO")
            .HasColumnType("VARCHAR2(50 CHAR)")
            .HasMaxLength(50)
            .IsUnicode(false)
            .IsRequired();

        builder.Property(entity => entity.OrderItemId)
            .HasColumnName("ORDER_ITEM_ID")
            .HasColumnType("NUMBER(19)");

        builder.Property(entity => entity.UserId)
            .HasColumnName("USER_ID")
            .HasColumnType("NUMBER(19)");

        builder.Property(entity => entity.QrCode)
            .HasColumnName("QR_CODE")
            .HasColumnType("VARCHAR2(255 CHAR)")
            .HasMaxLength(255)
            .IsUnicode(false)
            .IsRequired();

        builder.Property(entity => entity.AntiFakeCode)
            .HasColumnName("ANTI_FAKE_CODE")
            .HasColumnType("VARCHAR2(100 CHAR)")
            .HasMaxLength(100)
            .IsUnicode(false)
            .IsRequired();

        builder.Property(entity => entity.TicketStatus)
            .HasColumnName("TICKET_STATUS")
            .HasColumnType("VARCHAR2(20 CHAR)")
            .HasMaxLength(20)
            .IsUnicode(false)
            .HasDefaultValue("UNUSED")
            .IsRequired();

        builder.Property(entity => entity.CheckTime)
            .HasColumnName("CHECK_TIME")
            .HasColumnType("TIMESTAMP(6)");

        builder.Property(entity => entity.CheckDevice)
            .HasColumnName("CHECK_DEVICE")
            .HasColumnType("VARCHAR2(100 CHAR)")
            .HasMaxLength(100)
            .IsUnicode(false);

        builder.Property(entity => entity.CheckBy)
            .HasColumnName("CHECK_BY")
            .HasColumnType("VARCHAR2(50 CHAR)")
            .HasMaxLength(50)
            .IsUnicode(false);

        builder.ConfigureAuditableEntity();

        builder.HasIndex(entity => entity.ETicketNo)
            .IsUnique()
            .HasDatabaseName("UK_ETICKET_NO");

        builder.HasIndex(entity => entity.OrderItemId)
            .IsUnique()
            .HasDatabaseName("UK_ETICKET_ORDERITEM");

        builder.HasIndex(entity => entity.QrCode)
            .IsUnique()
            .HasDatabaseName("UK_ETICKET_QRCODE");

        builder.HasIndex(entity => entity.AntiFakeCode)
            .IsUnique()
            .HasDatabaseName("UK_ETICKET_ANTIFAKE");

        builder.HasIndex(entity => entity.UserId)
            .HasDatabaseName("IDX_ETICKET_USER");

        builder.HasIndex(entity => entity.TicketStatus)
            .HasDatabaseName("IDX_ETICKET_STATUS");

        builder.HasIndex(entity => entity.CheckTime)
            .HasDatabaseName("IDX_ETICKET_CHECKTIME");

        builder.HasOne(entity => entity.OrderItem)
            .WithOne(entity => entity.ETicket)
            .HasForeignKey<ETicket>(entity => entity.OrderItemId)
            .OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_ETICKET_ORDERITEM");

        builder.HasOne(entity => entity.User)
            .WithMany()
            .HasForeignKey(entity => entity.UserId)
            .OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_ETICKET_USER");
    }
}
