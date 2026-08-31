using System.Data;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using ShowtimeBackend.Data;
using ShowtimeBackend.Entities.OrderTicket;
using ShowtimeBackend.Entities.SeatZone;

namespace ShowtimeBackend.Services.OrderTicket;

public sealed class OracleExchangeLockCoordinator(AppDbContext dbContext)
    : IExchangeLockCoordinator
{
    public Task<bool> LockExchangeRequestAsync(long exchangeId, CancellationToken cancellationToken) =>
        LockAsync("EXCHANGE_REQUEST", "EXCHANGE_ID", exchangeId, cancellationToken);

    public Task<bool> LockOrderAsync(long orderId, CancellationToken cancellationToken) =>
        LockAsync("T_ORDER", "ORDER_ID", orderId, cancellationToken);

    public Task<bool> LockOrderItemAsync(long orderItemId, CancellationToken cancellationToken) =>
        LockAsync("ORDER_ITEM", "ORDER_ITEM_ID", orderItemId, cancellationToken);

    public Task<bool> LockETicketAsync(long eTicketId, CancellationToken cancellationToken) =>
        LockAsync("E_TICKET", "ETICKET_ID", eTicketId, cancellationToken);

    public Task<bool> LockSeatReservationAsync(
        long seatReservationId,
        CancellationToken cancellationToken) =>
        LockAsync(
            "SEAT_RESERVATION",
            "SEAT_RESERVATION_ID",
            seatReservationId,
            cancellationToken);

    public Task<bool> LockSeatLockAsync(long seatLockId, CancellationToken cancellationToken) =>
        LockAsync("SEAT_LOCK", "SEAT_LOCK_ID", seatLockId, cancellationToken);

    private async Task<bool> LockAsync(
        string table,
        string column,
        long id,
        CancellationToken cancellationToken)
    {
        _ = dbContext.Database.CurrentTransaction ?? throw new InvalidOperationException(
            "An existing database transaction is required before acquiring an exchange lock.");

        if (!dbContext.Database.IsOracle())
        {
            return table switch
            {
                "EXCHANGE_REQUEST" => await dbContext.Set<ExchangeRequest>()
                    .Where(item => item.ExchangeId == id)
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
                "ORDER_ITEM" => await dbContext.Set<OrderItem>()
                    .Where(item => item.OrderItemId == id)
                    .ExecuteUpdateAsync(
                        setters => setters.SetProperty(
                            item => item.UpdateBy,
                            item => item.UpdateBy),
                        cancellationToken) == 1,
                "E_TICKET" => await dbContext.Set<ETicket>()
                    .Where(item => item.ETicketId == id)
                    .ExecuteUpdateAsync(
                        setters => setters.SetProperty(
                            item => item.UpdateBy,
                            item => item.UpdateBy),
                        cancellationToken) == 1,
                "SEAT_RESERVATION" => await dbContext.Set<SeatReservation>()
                    .Where(item => item.SeatReservationId == id)
                    .ExecuteUpdateAsync(
                        setters => setters.SetProperty(
                            item => item.UpdateBy,
                            item => item.UpdateBy),
                        cancellationToken) == 1,
                "SEAT_LOCK" => await dbContext.Set<SeatLock>()
                    .Where(item => item.SeatLockId == id)
                    .ExecuteUpdateAsync(
                        setters => setters.SetProperty(
                            item => item.UpdateBy,
                            item => item.UpdateBy),
                        cancellationToken) == 1,
                _ => false,
            };
        }

        var currentTransaction = dbContext.Database.CurrentTransaction;
        var connection = dbContext.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        var targetSchema = ResolveTargetSchema(table);
        command.CommandText =
            $"SELECT {column} FROM {targetSchema}.{table} WHERE {column} = :id FOR UPDATE";
        command.CommandType = CommandType.Text;
        command.Transaction = currentTransaction!.GetDbTransaction();
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
            "EXCHANGE_REQUEST" => typeof(ExchangeRequest),
            "T_ORDER" => typeof(Order),
            "ORDER_ITEM" => typeof(OrderItem),
            "E_TICKET" => typeof(ETicket),
            "SEAT_RESERVATION" => typeof(SeatReservation),
            "SEAT_LOCK" => typeof(SeatLock),
            _ => throw new InvalidOperationException("Unsupported exchange lock table."),
        };
        var mappedType = dbContext.Model.FindEntityType(entityType) ??
            throw new InvalidOperationException("The exchange lock entity is not mapped.");
        var schema = mappedType.GetSchema() ?? dbContext.Model.GetDefaultSchema();
        if (string.IsNullOrWhiteSpace(schema) ||
            !Regex.IsMatch(schema, "^[A-Z][A-Z0-9_$#]{0,29}$", RegexOptions.CultureInvariant))
        {
            throw new InvalidOperationException(
                "The mapped exchange target schema is not a safe unquoted Oracle identifier.");
        }
        return schema;
    }
}
