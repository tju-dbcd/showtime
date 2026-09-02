using System.Data.Common;
using System.Text;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ShowtimeBackend.Tests.SeatZone;

internal sealed class SeatZoneCommandCounter : DbCommandInterceptor
{
    private readonly object _sync = new();
    private readonly List<string> _readCommands = [];

    public int ReadCommandCount
    {
        get
        {
            lock (_sync)
            {
                return _readCommands.Count;
            }
        }
    }

    public IReadOnlyList<string> ReadCommands
    {
        get
        {
            lock (_sync)
            {
                return _readCommands.ToArray();
            }
        }
    }

    public void Reset()
    {
        lock (_sync)
        {
            _readCommands.Clear();
        }
    }

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result)
    {
        RecordReadCommand(command.CommandText);
        return base.ReaderExecuting(command, eventData, result);
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        RecordReadCommand(command.CommandText);
        return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }

    private void RecordReadCommand(string commandText)
    {
        var normalized = NormalizeSql(commandText);
        if (!normalized.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase) &&
            !normalized.StartsWith("WITH", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        lock (_sync)
        {
            _readCommands.Add(normalized);
        }
    }

    private static string NormalizeSql(string sql)
    {
        var normalized = new StringBuilder(sql.Length);
        var previousWasWhitespace = false;
        foreach (var character in sql.Trim())
        {
            if (char.IsWhiteSpace(character))
            {
                if (!previousWasWhitespace)
                {
                    normalized.Append(' ');
                }

                previousWasWhitespace = true;
                continue;
            }

            normalized.Append(character);
            previousWasWhitespace = false;
        }

        return normalized.ToString();
    }
}
