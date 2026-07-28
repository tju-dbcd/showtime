using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShowtimeBackend.Entities.UserPermission;

namespace ShowtimeBackend.Data.Configurations.UserPermission;

public class OperationLogConfiguration : IEntityTypeConfiguration<OperationLog>
{
    public void Configure(EntityTypeBuilder<OperationLog> builder)
    {
        builder.ToTable("OPERATION_LOG", table =>
        {
            table.HasCheckConstraint(
                "CK_OP_LOG_STATUS",
                "STATUS IN (0, 1)");
        });

        builder.HasKey(entity => entity.LogId)
            .HasName("PK_OPERATION_LOG");

        builder.Property(entity => entity.LogId)
            .HasColumnName("LOG_ID")
            .HasColumnType("NUMBER(19)")
            .ValueGeneratedOnAdd();

        builder.Property(entity => entity.UserId)
            .HasColumnName("USER_ID")
            .HasColumnType("NUMBER(19)");

        builder.Property(entity => entity.UserName)
            .HasColumnName("USER_NAME")
            .HasColumnType("VARCHAR2(50 CHAR)")
            .HasMaxLength(50)
            .IsUnicode(false);

        builder.Property(entity => entity.ShowId)
            .HasColumnName("SHOW_ID")
            .HasColumnType("NUMBER(19)");

        builder.Property(entity => entity.OperationModule)
            .HasColumnName("OPERATION_MODULE")
            .HasColumnType("VARCHAR2(50 CHAR)")
            .HasMaxLength(50)
            .IsUnicode(false)
            .IsRequired();

        builder.Property(entity => entity.OperationType)
            .HasColumnName("OPERATION_TYPE")
            .HasColumnType("VARCHAR2(30 CHAR)")
            .HasMaxLength(30)
            .IsUnicode(false)
            .IsRequired();

        builder.Property(entity => entity.RequestUrl)
            .HasColumnName("REQUEST_URL")
            .HasColumnType("VARCHAR2(500 CHAR)")
            .HasMaxLength(500)
            .IsUnicode(false);

        builder.Property(entity => entity.RequestParams)
            .HasColumnName("REQUEST_PARAMS")
            .HasColumnType("CLOB")
            .IsUnicode(false);

        builder.Property(entity => entity.ResponseResult)
            .HasColumnName("RESPONSE_RESULT")
            .HasColumnType("CLOB")
            .IsUnicode(false);

        builder.Property(entity => entity.IpAddress)
            .HasColumnName("IP_ADDRESS")
            .HasColumnType("VARCHAR2(50 CHAR)")
            .HasMaxLength(50)
            .IsUnicode(false);

        builder.Property(entity => entity.UserAgent)
            .HasColumnName("USER_AGENT")
            .HasColumnType("VARCHAR2(500 CHAR)")
            .HasMaxLength(500)
            .IsUnicode(false);

        builder.Property(entity => entity.CostTime)
            .HasColumnName("COST_TIME")
            .HasColumnType("NUMBER(10)");

        builder.Property(entity => entity.Status)
            .HasColumnName("STATUS")
            .HasColumnType("NUMBER(1)")
            .HasDefaultValue(true)
            .HasSentinel(true)
            .IsRequired();

        builder.Property(entity => entity.ErrorMsg)
            .HasColumnName("ERROR_MSG")
            .HasColumnType("VARCHAR2(500 CHAR)")
            .HasMaxLength(500)
            .IsUnicode(false);

        builder.ConfigureAuditableEntity();

        builder.HasIndex(entity => entity.UserId)
            .HasDatabaseName("IDX_OP_LOG_USER");

        builder.HasIndex(entity => entity.ShowId)
            .HasDatabaseName("IDX_OP_LOG_SHOW");

        builder.HasIndex(entity => entity.CreateTime)
            .HasDatabaseName("IDX_OP_LOG_TIME");

        builder.HasIndex(entity => entity.OperationType)
            .HasDatabaseName("IDX_OP_LOG_TYPE");

        builder.HasOne(entity => entity.User)
            .WithMany(entity => entity.OperationLogs)
            .HasForeignKey(entity => entity.UserId)
            .OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_OP_LOG_USER");
    }
}
