using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using ShowtimeBackend.Data;

namespace ShowtimeBackend.Services.OrderTicket;

public sealed class OracleRefundLockCoordinator(AppDbContext dbContext)
    : IRefundLockCoordinator
{
    private const string RefundRequestLockSql =
        "SELECT REFUND_ID FROM APP_OWNER.REFUND_REQUEST " +
        "WHERE REFUND_ID = :id FOR UPDATE";

    private const string OrderLockSql =
        "SELECT ORDER_ID FROM APP_OWNER.T_ORDER " +
        "WHERE ORDER_ID = :id FOR UPDATE";

    public Task<bool> LockRefundRequestAsync(
        long refundId,
        CancellationToken cancellationToken) => LockAsync(
            RefundRequestLockSql,
            refundId,
            cancellationToken);

    public Task<bool> LockOrderAsync(
        long orderId,
        CancellationToken cancellationToken) => LockAsync(
            OrderLockSql,
            orderId,
            cancellationToken);

    private async Task<bool> LockAsync(
        string sql,
        long id,
        CancellationToken cancellationToken)
    {
        var currentTransaction = dbContext.Database.CurrentTransaction ??
            throw new InvalidOperationException(
                "An existing database transaction is required before acquiring a refund lock.");
        var connection = dbContext.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandType = CommandType.Text;
        command.Transaction = currentTransaction.GetDbTransaction();

        var parameter = command.CreateParameter();
        parameter.ParameterName = "id";
        parameter.DbType = DbType.Int64;
        parameter.Value = id;
        command.Parameters.Add(parameter);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is not null and not DBNull;
    }
}
