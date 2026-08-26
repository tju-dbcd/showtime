using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using ShowtimeBackend.Common.IdentityData;
using ShowtimeBackend.Entities.UserPermission;

namespace ShowtimeBackend.Data.Interceptors;

public sealed class UserRealNameEncryptionInterceptor(
    IIdentityDataProtector protector) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        ProtectPendingValues(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ProtectPendingValues(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void ProtectPendingValues(DbContext? dbContext)
    {
        if (dbContext is null)
        {
            return;
        }

        foreach (var entry in dbContext.ChangeTracker.Entries<UserRealName>())
        {
            if (!RequiresProtection(entry))
            {
                continue;
            }

            var value = entry.Entity.IdCardNo;
            if (string.IsNullOrEmpty(value) || protector.IsProtected(value))
            {
                continue;
            }

            entry.Entity.IdCardNo = protector.Protect(value);
        }
    }

    private static bool RequiresProtection(EntityEntry<UserRealName> entry) =>
        entry.State == EntityState.Added ||
        entry.State == EntityState.Modified &&
        entry.Property(entity => entity.IdCardNo).IsModified;
}
