using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Oracle.ManagedDataAccess.Client;
using ShowtimeBackend.Data;
using ShowtimeBackend.Entities.SeatZone;

namespace ShowtimeBackend.Services.SeatZone;

internal sealed class OracleSeatLockBatchWriter : ISeatLockBatchWriter
{
    internal const int ArrayBindBatchSize = 500;

    internal static IEnumerable<SeatLock[]> CreateBatches(IReadOnlyList<SeatLock> locks)
    {
        for (var offset = 0; offset < locks.Count; offset += ArrayBindBatchSize)
        {
            var count = Math.Min(ArrayBindBatchSize, locks.Count - offset);
            var batch = new SeatLock[count];
            for (var index = 0; index < count; index++)
            {
                batch[index] = locks[offset + index];
            }

            yield return batch;
        }
    }

    public bool CanWrite(AppDbContext dbContext)
    {
        return dbContext.Database.ProviderName?.Contains("Oracle", StringComparison.OrdinalIgnoreCase) == true
            && dbContext.Database.GetDbConnection() is OracleConnection
            && dbContext.Database.CurrentTransaction?.GetDbTransaction() is OracleTransaction;
    }

    public async Task InsertAsync(
        AppDbContext dbContext,
        IReadOnlyList<SeatLock> locks,
        CancellationToken cancellationToken)
    {
        if (!CanWrite(dbContext))
        {
            throw new InvalidOperationException(
                "Oracle seat-lock batch writing requires an Oracle provider, connection, and transaction.");
        }

        if (locks.Count == 0)
        {
            return;
        }

        var connection = (OracleConnection)dbContext.Database.GetDbConnection();
        var transaction = (OracleTransaction)dbContext.Database.CurrentTransaction!.GetDbTransaction();

        foreach (var batch in CreateBatches(locks))
        {
            using var command = connection.CreateCommand();
            command.BindByName = true;
            command.ArrayBindCount = batch.Length;
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO "APP_OWNER"."SEAT_LOCK"
                    (SESSION_ID, SEAT_ID, USER_ID, LOCK_TOKEN, LOCK_STATUS,
                     LOCK_TIME, EXPIRE_TIME, RELEASE_TIME, REMARK, CREATE_BY, UPDATE_BY)
                VALUES
                    (:session_id, :seat_id, :user_id, :lock_token, :lock_status,
                     :lock_time, :expire_time, :release_time, :remark, :create_by, :update_by)
                """;

            command.Parameters.Add(CreateNumberParameter("session_id", batch.Select(item => item.SessionId).ToArray()));
            command.Parameters.Add(CreateNumberParameter("seat_id", batch.Select(item => item.SeatId).ToArray()));
            command.Parameters.Add(CreateNumberParameter("user_id", batch.Select(item => item.UserId).ToArray()));
            command.Parameters.Add(CreateStringParameter("lock_token", batch.Select(item => item.LockToken).ToArray(), 64));
            command.Parameters.Add(CreateStringParameter("lock_status", batch.Select(item => item.LockStatus).ToArray(), 20));
            command.Parameters.Add(CreateTimestampParameter("lock_time", batch.Select(item => item.LockTime).ToArray()));
            command.Parameters.Add(CreateTimestampParameter("expire_time", batch.Select(item => item.ExpireTime).ToArray()));
            command.Parameters.Add(CreateTimestampParameter(
                "release_time",
                batch.Select(item => item.ReleaseTime.HasValue ? item.ReleaseTime.Value : (object)DBNull.Value).ToArray()));
            command.Parameters.Add(CreateStringParameter(
                "remark",
                batch.Select(item => item.Remark is null ? (object)DBNull.Value : item.Remark).ToArray(),
                255));
            command.Parameters.Add(CreateStringParameter(
                "create_by",
                batch.Select(item => item.CreateBy is null ? (object)DBNull.Value : item.CreateBy).ToArray(),
                50));
            command.Parameters.Add(CreateStringParameter(
                "update_by",
                batch.Select(item => item.UpdateBy is null ? (object)DBNull.Value : item.UpdateBy).ToArray(),
                50));

            var affectedRows = await command.ExecuteNonQueryAsync(cancellationToken);
            if (affectedRows >= 0 && affectedRows != batch.Length)
            {
                throw new InvalidOperationException(
                    $"Oracle seat-lock batch insert affected {affectedRows} rows for a batch of {batch.Length}.");
            }
        }
    }

    private static OracleParameter CreateNumberParameter(string name, long[] values)
    {
        return new OracleParameter(name, OracleDbType.Int64)
        {
            Value = values
        };
    }

    private static OracleParameter CreateTimestampParameter(string name, DateTime[] values)
    {
        return new OracleParameter(name, OracleDbType.TimeStamp)
        {
            Value = values
        };
    }

    private static OracleParameter CreateTimestampParameter(string name, object[] values)
    {
        return new OracleParameter(name, OracleDbType.TimeStamp)
        {
            Value = values
        };
    }

    private static OracleParameter CreateStringParameter(string name, string[] values, int maxLength)
    {
        return new OracleParameter(name, OracleDbType.Varchar2, maxLength)
        {
            Value = values,
            Size = maxLength,
            ArrayBindSize = Enumerable.Repeat(maxLength, values.Length).ToArray()
        };
    }

    private static OracleParameter CreateStringParameter(string name, object[] values, int maxLength)
    {
        return new OracleParameter(name, OracleDbType.Varchar2, maxLength)
        {
            Value = values,
            Size = maxLength,
            ArrayBindSize = Enumerable.Repeat(maxLength, values.Length).ToArray()
        };
    }
}
