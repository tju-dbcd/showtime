using System.Data;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using ShowtimeBackend.Data;
using ShowtimeBackend.Entities.OrderTicket;

namespace ShowtimeBackend.Services.OrderTicket;

public sealed class OracleRefundLockCoordinator(AppDbContext dbContext)
    : IRefundLockCoordinator
{
    public Task<bool> LockRefundRequestAsync(
        long refundId,
        CancellationToken cancellationToken) => LockAsync(
            "REFUND_REQUEST",
            "REFUND_ID",
            refundId,
            cancellationToken);

    public Task<bool> LockOrderAsync(
        long orderId,
        CancellationToken cancellationToken) => LockAsync(
            "T_ORDER",
            "ORDER_ID",
            orderId,
            cancellationToken);

    private async Task<bool> LockAsync(
        string table,
        string column,
        long id,
        CancellationToken cancellationToken)
    {
        var currentTransaction = dbContext.Database.CurrentTransaction ??
            throw new InvalidOperationException(
                "An existing database transaction is required before acquiring a refund lock.");
        if (!dbContext.Database.IsOracle())
        {
            return table switch
            {
                "REFUND_REQUEST" => await dbContext.Set<RefundRequest>()
                    .Where(item => item.RefundId == id)
                    .ExecuteUpdateAsync(
                        setters => setters.SetProperty(
                            item => item.UpdateBy,
                            item => item.UpdateBy),
                        cancellationToken) == 1,
                "T_ORDER" => await dbContext.Set<Order>()
                    .Where(item => item.OrderId == id)
                    .ExecuteUpdateAsync(
                        setters => setters.SetProperty(
                            item => item.UpdateBy,
                            item => item.UpdateBy),
                        cancellationToken) == 1,
                _ => false,
            };
        }

        var connection = dbContext.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        var schema = ResolveTargetSchema(table);
        command.CommandText =
            $"SELECT {column} FROM {schema}.{table} WHERE {column} = :id FOR UPDATE";
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

    private string ResolveTargetSchema(string table)
    {
        var entityType = table switch
        {
            "REFUND_REQUEST" => typeof(RefundRequest),
            "T_ORDER" => typeof(Order),
            _ => throw new InvalidOperationException("Unsupported refund lock table."),
        };
        var mappedType = dbContext.Model.FindEntityType(entityType) ??
            throw new InvalidOperationException("The refund lock entity is not mapped.");
        var schema = mappedType.GetSchema() ?? dbContext.Model.GetDefaultSchema();
        if (string.IsNullOrWhiteSpace(schema) ||
            !Regex.IsMatch(
                schema,
                "^[A-Z][A-Z0-9_$#]{0,29}$",
                RegexOptions.CultureInvariant))
        {
            throw new InvalidOperationException(
                "The mapped refund target schema is not a safe Oracle identifier.");
        }
        return schema;
    }
}
