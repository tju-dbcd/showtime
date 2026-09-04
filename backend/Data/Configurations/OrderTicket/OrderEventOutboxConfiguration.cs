using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShowtimeBackend.Data.Configurations.UserPermission;
using ShowtimeBackend.Entities.OrderTicket;

namespace ShowtimeBackend.Data.Configurations.OrderTicket;

public sealed class OrderEventOutboxConfiguration : IEntityTypeConfiguration<OrderEventOutbox>
{
    public void Configure(EntityTypeBuilder<OrderEventOutbox> builder)
    {
        builder.ToTable("T_ORDER_EVENT_OUTBOX", table => table.HasCheckConstraint(
            "CHK_ORDER_OUTBOX_STATUS",
            "STATUS IN ('PENDING', 'PROCESSING', 'PUBLISHED', 'FAILED')"));
        builder.HasKey(item => item.EventId).HasName("PK_ORDER_EVENT_OUTBOX");
        builder.Property(item => item.EventId).HasColumnName("EVENT_ID").HasColumnType("CHAR(36 CHAR)").HasMaxLength(36).IsFixedLength().IsUnicode(false);
        builder.Property(item => item.EventType).HasColumnName("EVENT_TYPE").HasColumnType("VARCHAR2(50 CHAR)").HasMaxLength(50).IsUnicode(false).IsRequired();
        builder.Property(item => item.RoutingKey).HasColumnName("ROUTING_KEY").HasColumnType("VARCHAR2(100 CHAR)").HasMaxLength(100).IsUnicode(false).IsRequired();
        builder.Property(item => item.AggregateId).HasColumnName("AGGREGATE_ID").HasColumnType("NUMBER(19)");
        builder.Property(item => item.UserId).HasColumnName("USER_ID").HasColumnType("NUMBER(19)");
        builder.Property(item => item.Payload).HasColumnName("PAYLOAD").HasColumnType("CLOB").IsRequired();
        builder.Property(item => item.OccurredAt).HasColumnName("OCCURRED_AT").HasColumnType("TIMESTAMP(6)").IsRequired();
        builder.Property(item => item.Status).HasColumnName("STATUS").HasColumnType("VARCHAR2(20 CHAR)").HasMaxLength(20).IsUnicode(false).IsRequired();
        builder.Property(item => item.AttemptCount).HasColumnName("ATTEMPT_COUNT").HasColumnType("NUMBER(5)").HasDefaultValue(0).IsRequired();
        builder.Property(item => item.NextAttemptAt).HasColumnName("NEXT_ATTEMPT_AT").HasColumnType("TIMESTAMP(6)").IsRequired();
        builder.Property(item => item.LockedUntil).HasColumnName("LOCKED_UNTIL").HasColumnType("TIMESTAMP(6)");
        builder.Property(item => item.LockOwner).HasColumnName("LOCK_OWNER").HasColumnType("VARCHAR2(100 CHAR)").HasMaxLength(100).IsUnicode(false);
        builder.Property(item => item.PublishedAt).HasColumnName("PUBLISHED_AT").HasColumnType("TIMESTAMP(6)");
        builder.Property(item => item.LastError).HasColumnName("LAST_ERROR").HasColumnType("VARCHAR2(1000 CHAR)").HasMaxLength(1000).IsUnicode(false);
        builder.ConfigureAuditableEntity();
        builder.HasIndex(item => new { item.Status, item.NextAttemptAt, item.EventId })
            .HasDatabaseName("IDX_ORDER_OUTBOX_RETRY");
        builder.HasIndex(item => new { item.AggregateId, item.EventType })
            .HasDatabaseName("IDX_ORDER_OUTBOX_AGGREGATE");
    }
}
