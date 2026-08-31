using Microsoft.EntityFrameworkCore;
using ShowtimeBackend.Data;

namespace ShowtimeBackend.Tests;

internal sealed class SqliteAuthDbContext(DbContextOptions<SqliteAuthDbContext> options)
    : AppDbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        var properties = modelBuilder.Model
            .GetEntityTypes()
            .SelectMany(entityType => entityType.GetProperties())
            .ToList();

        foreach (var property in properties)
        {
            var clrType = Nullable.GetUnderlyingType(property.ClrType)
                ?? property.ClrType;
            var sqliteColumnType = clrType switch
            {
                _ when clrType == typeof(string) => "TEXT",
                _ when clrType == typeof(byte[]) => "BLOB",
                _ when clrType == typeof(float) || clrType == typeof(double) => "REAL",
                _ when clrType == typeof(decimal) => "NUMERIC",
                _ when clrType == typeof(DateTime) ||
                    clrType == typeof(DateTimeOffset) ||
                    clrType == typeof(TimeSpan) ||
                    clrType == typeof(Guid) => "TEXT",
                _ => "INTEGER",
            };

            modelBuilder.Entity(property.DeclaringType.ClrType)
                .Property(property.Name)
                .HasColumnType(sqliteColumnType);
        }
    }
}
