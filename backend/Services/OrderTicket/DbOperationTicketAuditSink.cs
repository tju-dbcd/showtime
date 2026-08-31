using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ShowtimeBackend.Data;
using ShowtimeBackend.Entities.UserPermission;

namespace ShowtimeBackend.Services.OrderTicket;

/// <summary>
/// 领域审计事件落库实现：每个事件通过 <see cref="IDbContextFactory{TContext}"/> 创建独立
/// <see cref="AppDbContext"/> 实例追加写入 OPERATION_LOG，审计写入绝不卷入业务事务；
/// 事件完整快照（含 OrderId/RefundId/ActualRefund/Metadata）以 JSON 存入 REQUEST_PARAMS 承载。
/// </summary>
public sealed class DbOperationTicketAuditSink(IDbContextFactory<AppDbContext> dbFactory)
    : IOrderTicketAuditSink
{
    /// <summary>领域事件统一归属模块（对应 OPERATION_LOG.OPERATION_MODULE）。</summary>
    public const string OperationModule = "ORDER_TICKET";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async ValueTask WriteAsync(
        OrderTicketAuditEvent auditEvent,
        CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        db.OperationLogs.Add(ToOperationLog(auditEvent));
        await db.SaveChangesAsync(cancellationToken);
    }

    private static OperationLog ToOperationLog(OrderTicketAuditEvent auditEvent) =>
        new()
        {
            UserName = auditEvent.Actor,
            OperationModule = OperationModule,
            OperationType = auditEvent.Operation,
            RequestParams = JsonSerializer.Serialize(auditEvent, JsonOptions),
            CreateBy = auditEvent.Actor,
            UpdateBy = auditEvent.Actor,
        };
}
