using ShowtimeBackend.Entities.ShowSessions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ShowtimeBackend.Data.Configurations.ShowSessions
{
    public class ShowSessionConfiguration : IEntityTypeConfiguration<ShowtimeBackend.Entities.ShowSessions.ShowSession>
    {
        public void Configure(EntityTypeBuilder<ShowtimeBackend.Entities.ShowSessions.ShowSession> builder)
        {
            // 映射表名
            builder.ToTable("SHOW_SESSION");

            // 主键配置
            builder.HasKey(s => s.SessionId);
            builder.Property(s => s.SessionId)
                   .HasColumnName("SESSION_ID")
                   .HasColumnType("NUMBER(19)")
                   .ValueGeneratedOnAdd(); // 对应自增主键

            // 业务与外键关联字段
            builder.Property(s => s.ShowId)
                   .HasColumnName("SHOW_ID")
                   .HasColumnType("NUMBER(19)")
                   .IsRequired();

            builder.Property(s => s.SeatMapId)
                   .HasColumnName("SEAT_MAP_ID")
                   .HasColumnType("NUMBER(19)")
                   .IsRequired();

            // 时间字段配置
            builder.Property(s => s.StartTime)
                   .HasColumnName("START_TIME")
                   .HasColumnType("TIMESTAMP(6)")
                   .IsRequired();

            builder.Property(s => s.EndTime)
                   .HasColumnName("END_TIME")
                   .HasColumnType("TIMESTAMP(6)")
                   .IsRequired();

            builder.Property(s => s.SaleStartTime)
                   .HasColumnName("SALE_START_TIME")
                   .HasColumnType("TIMESTAMP(6)")
                   .IsRequired();

            builder.Property(s => s.SaleEndTime)
                   .HasColumnName("SALE_END_TIME")
                   .HasColumnType("TIMESTAMP(6)")
                   .IsRequired();

            // 状态与默认值
            builder.Property(s => s.SessionStatus)
                   .HasColumnName("SESSION_STATUS")
                   .HasColumnType("VARCHAR2(20 CHAR)")
                   .HasMaxLength(20)
                   .HasDefaultValue("UPCOMING")
                   .IsRequired();

            // 审计字段配置
            builder.Property(s => s.CreateTime)
                   .HasColumnName("CREATE_TIME")
                   .HasColumnType("TIMESTAMP(6)")
                   .HasDefaultValueSql("CURRENT_TIMESTAMP")
                   .IsRequired();

            builder.Property(s => s.UpdateTime)
                   .HasColumnName("UPDATE_TIME")
                   .HasColumnType("TIMESTAMP(6)")
                   .HasDefaultValueSql("CURRENT_TIMESTAMP")
                   .IsRequired();

            builder.Property(s => s.CreateBy)
                   .HasColumnName("CREATE_BY")
                   .HasColumnType("VARCHAR2(50 CHAR)")
                   .HasMaxLength(50)
                   .IsRequired(false);

            builder.Property(s => s.UpdateBy)
                   .HasColumnName("UPDATE_BY")
                   .HasColumnType("VARCHAR2(50 CHAR)")
                   .HasMaxLength(50)
                   .IsRequired(false);

            // 外键关系（与 SHOW 主表建立一对多关系）
            builder.HasOne(s => s.Show)
                   .WithMany()
                   .HasForeignKey(s => s.ShowId)
                   .OnDelete(DeleteBehavior.Restrict); // 保护机制：删除 SHOW 时阻止直接级联删场次，避免数据灾难
        }
    }
}
