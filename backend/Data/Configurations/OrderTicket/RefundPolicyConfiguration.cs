using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShowtimeBackend.Data.Configurations.UserPermission;
using ShowtimeBackend.Entities.OrderTicket;

namespace ShowtimeBackend.Data.Configurations.OrderTicket;

public class RefundPolicyConfiguration : IEntityTypeConfiguration<RefundPolicy>
{
    public void Configure(EntityTypeBuilder<RefundPolicy> builder)
    {
        builder.ToTable("REFUND_POLICY", table =>
        {
            table.HasCheckConstraint(
                "CHK_REFUND_POLICY_RATE",
                "REFUND_RATE BETWEEN 0 AND 1");
            table.HasCheckConstraint(
                "CHK_REFUND_POLICY_FEE",
                "SERVICE_FEE >= 0");
            table.HasCheckConstraint(
                "CHK_REFUND_POLICY_PRIORITY",
                "PRIORITY > 0");
            table.HasCheckConstraint(
                "CHK_REFUND_POLICY_STATUS",
                "STATUS IN (0, 1)");
        });

        builder.HasKey(entity => entity.PolicyId)
            .HasName("PK_REFUND_POLICY");

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

        builder.Property(entity => entity.RefundDeadlineHour)
            .HasColumnName("REFUND_DEADLINE_HOUR")
            .HasColumnType("NUMBER(5)")
            .IsRequired();

        builder.Property(entity => entity.RefundRate)
            .HasColumnName("REFUND_RATE")
            .HasColumnType("NUMBER(5,4)")
            .HasPrecision(5, 4)
            .HasDefaultValue(1m)
            .HasSentinel(-1m)
            .IsRequired();

        builder.Property(entity => entity.ServiceFee)
            .HasColumnName("SERVICE_FEE")
            .HasColumnType("NUMBER(10,2)")
            .HasPrecision(10, 2)
            .HasDefaultValue(0m)
            .IsRequired();

        builder.Property(entity => entity.Priority)
            .HasColumnName("PRIORITY")
            .HasColumnType("NUMBER(5)")
            .HasDefaultValue(1)
            .IsRequired();

        builder.Property(entity => entity.Status)
            .HasColumnName("STATUS")
            // NUMBER(3)：Oracle 提供器把 NUMBER(1) 保留为 bool 的默认存储类型，
            // byte 属性声明 NUMBER(1) 会在快照构造时触发 Byte→Boolean 强转崩溃（ORA 环境实测）
            .HasColumnType("NUMBER(3)")
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
            .HasDatabaseName("IDX_REFUND_POLICY_SHOW");

        builder.HasIndex(entity => entity.Status)
            .HasDatabaseName("IDX_REFUND_POLICY_STATUS");

        builder.HasIndex(entity => entity.Priority)
            .HasDatabaseName("IDX_REFUND_POLICY_PRIORITY");
    }
}
