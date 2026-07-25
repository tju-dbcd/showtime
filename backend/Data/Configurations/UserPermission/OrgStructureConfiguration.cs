using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShowtimeBackend.Entities.UserPermission;

namespace ShowtimeBackend.Data.Configurations.UserPermission;

public class OrgStructureConfiguration : IEntityTypeConfiguration<OrgStructure>
{
    public void Configure(EntityTypeBuilder<OrgStructure> builder)
    {
        builder.ToTable("ORG_STRUCTURE", table =>
        {
            table.HasCheckConstraint(
                "CK_ORG_STRUCTURE_TYPE",
                "ORG_TYPE IN ('COMPANY', 'DEPT', 'TEAM', 'OTHER')");
            table.HasCheckConstraint(
                "CK_ORG_STRUCTURE_STATUS",
                "STATUS IN (0, 1)");
        });

        builder.HasKey(entity => entity.OrgId)
            .HasName("PK_ORG_STRUCTURE");

        builder.Property(entity => entity.OrgId)
            .HasColumnName("ORG_ID")
            .HasColumnType("NUMBER(19)")
            .ValueGeneratedOnAdd();

        builder.Property(entity => entity.ParentId)
            .HasColumnName("PARENT_ID")
            .HasColumnType("NUMBER(19)");

        builder.Property(entity => entity.OrgCode)
            .HasColumnName("ORG_CODE")
            .HasColumnType("VARCHAR2(50 CHAR)")
            .HasMaxLength(50)
            .IsUnicode(false)
            .IsRequired();

        builder.Property(entity => entity.OrgName)
            .HasColumnName("ORG_NAME")
            .HasColumnType("VARCHAR2(100 CHAR)")
            .HasMaxLength(100)
            .IsUnicode(false)
            .IsRequired();

        builder.Property(entity => entity.OrgType)
            .HasColumnName("ORG_TYPE")
            .HasColumnType("VARCHAR2(20 CHAR)")
            .HasMaxLength(20)
            .IsUnicode(false)
            .HasDefaultValue("DEPT")
            .IsRequired();

        builder.Property(entity => entity.SortOrder)
            .HasColumnName("SORT_ORDER")
            .HasColumnType("NUMBER(5)")
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(entity => entity.Status)
            .HasColumnName("STATUS")
            .HasColumnType("NUMBER(1)")
            .HasDefaultValue(true)
            .HasSentinel(true)
            .IsRequired();

        builder.ConfigureAuditableEntity();

        builder.HasIndex(entity => entity.OrgCode)
            .IsUnique()
            .HasDatabaseName("UK_ORG_STRUCTURE_CODE");

        builder.HasIndex(entity => entity.ParentId)
            .HasDatabaseName("IDX_ORG_PARENT");

        builder.HasOne(entity => entity.Parent)
            .WithMany(entity => entity.Children)
            .HasForeignKey(entity => entity.ParentId)
            .OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_ORG_STRUCTURE_PARENT");
    }
}
