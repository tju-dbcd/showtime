using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ShowtimeBackend.Common.IdentityData;
using ShowtimeBackend.Data;
using ShowtimeBackend.Data.Interceptors;
using ShowtimeBackend.Entities.UserPermission;

const int BatchSize = 100;

var apply = args.Any(argument =>
    string.Equals(argument, "--apply", StringComparison.OrdinalIgnoreCase));
if (args.Any(argument =>
        !string.Equals(argument, "--apply", StringComparison.OrdinalIgnoreCase)))
{
    Console.Error.WriteLine("Usage: dotnet run [--apply]");
    return 2;
}

var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Oracle");
if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.Error.WriteLine(
        "ConnectionStrings__Oracle is required. The value was not read or displayed.");
    return 2;
}

var identityOptions = new IdentityDataOptions
{
    EncryptionKey = Environment.GetEnvironmentVariable(
        "IdentityData__EncryptionKey") ?? string.Empty,
};
var validation = new IdentityDataOptionsValidator().Validate(
    Options.DefaultName,
    identityOptions);
if (validation.Failed)
{
    Console.Error.WriteLine(validation.FailureMessage);
    return 2;
}

using var protector = new AesGcmIdentityDataProtector(
    Options.Create(identityOptions));
var interceptor = new UserRealNameEncryptionInterceptor(protector);
var dbOptions = new DbContextOptionsBuilder<AppDbContext>()
    .UseOracle(connectionString)
    .AddInterceptors(interceptor)
    .Options;
await using var dbContext = new AppDbContext(dbOptions);

var legacyCount = await dbContext.Set<UserRealName>()
    .AsNoTracking()
    .CountAsync(record => !record.IdCardNo.StartsWith("v1."));
Console.WriteLine($"Legacy USER_REAL_NAME rows found: {legacyCount}");

if (!apply)
{
    Console.WriteLine("Dry-run only. Re-run with --apply after backup and review.");
    return 0;
}

var migratedCount = 0;
while (true)
{
    var batch = await dbContext.Set<UserRealName>()
        .Where(record => !record.IdCardNo.StartsWith("v1."))
        .OrderBy(record => record.RealNameId)
        .Take(BatchSize)
        .ToListAsync();
    if (batch.Count == 0)
    {
        break;
    }

    foreach (var record in batch)
    {
        var legacy = protector.Unprotect(record.IdCardNo);
        if (!legacy.IsLegacy)
        {
            throw new InvalidOperationException(
                $"Record {record.RealNameId} was expected to be a legacy value.");
        }

        record.IdCardNo = legacy.Value;
        record.UpdateBy = "IdentityDataMigration";
        dbContext.Entry(record).Property(item => item.IdCardNo).IsModified = true;
    }

    var firstId = batch[0].RealNameId;
    var lastId = batch[^1].RealNameId;
    await dbContext.SaveChangesAsync();
    migratedCount += batch.Count;
    Console.WriteLine(
        $"Migrated {batch.Count} rows (REAL_NAME_ID {firstId}..{lastId}).");
    dbContext.ChangeTracker.Clear();
}

var remainingCount = await dbContext.Set<UserRealName>()
    .AsNoTracking()
    .CountAsync(record => !record.IdCardNo.StartsWith("v1."));
Console.WriteLine($"Migration complete. Migrated: {migratedCount}; remaining legacy: {remainingCount}.");
return remainingCount == 0 ? 0 : 1;
