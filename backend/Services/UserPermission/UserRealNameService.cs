using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Oracle.ManagedDataAccess.Client;
using ShowtimeBackend.Common.IdentityData;
using ShowtimeBackend.Data;
using ShowtimeBackend.DTOs.UserPermission;
using ShowtimeBackend.Entities.OrderTicket;
using ShowtimeBackend.Entities.UserPermission;

namespace ShowtimeBackend.Services.UserPermission;

public sealed partial class UserRealNameService(
    AppDbContext dbContext,
    IIdentityDataProtector protector,
    ILogger<UserRealNameService> logger) : IUserRealNameService
{
    public async Task<UserRealNameResult<IReadOnlyList<UserRealNameResponse>>> ListAsync(
        long userId,
        CancellationToken cancellationToken)
    {
        var records = await dbContext.Set<UserRealName>()
            .AsNoTracking()
            .Where(item => item.UserId == userId)
            .OrderByDescending(item => item.IsDefault)
            .ThenBy(item => item.RealNameId)
            .ToListAsync(cancellationToken);

        try
        {
            return UserRealNameResult<IReadOnlyList<UserRealNameResponse>>.Success(
                records.Select(ToResponse).ToList());
        }
        catch (IdentityDataProtectionException exception)
        {
            return ProtectionFailure<IReadOnlyList<UserRealNameResponse>>(
                userId,
                exception);
        }
    }

    public async Task<UserRealNameResult<UserRealNameResponse>> CreateAsync(
        long userId,
        string actor,
        CreateUserRealNameRequest request,
        CancellationToken cancellationToken)
    {
        var normalized = Normalize(request.RealName, request.IdCardNo);
        if (!normalized.IsValid)
        {
            return Invalid(normalized.ErrorCode!, normalized.Message!);
        }

        var records = await dbContext.Set<UserRealName>()
            .Where(item => item.UserId == userId)
            .OrderBy(item => item.RealNameId)
            .ToListAsync(cancellationToken);

        try
        {
            if (ContainsIdCard(records, normalized.IdCardNo!, exceptRealNameId: null))
            {
                return Conflict(
                    "REAL_NAME_DUPLICATE_ID_CARD",
                    "The identity-card number is already saved for this user.");
            }
        }
        catch (IdentityDataProtectionException exception)
        {
            return ProtectionFailure<UserRealNameResponse>(userId, exception);
        }

        var makeDefault = records.Count == 0 || request.IsDefault;
        await using var transaction = dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;

        try
        {
            if (makeDefault)
            {
                foreach (var existingRecord in records.Where(item => item.IsDefault))
                {
                    existingRecord.IsDefault = false;
                    existingRecord.UpdateBy = actor;
                }

                if (records.Any(item => dbContext.Entry(item).Property(x => x.IsDefault).IsModified))
                {
                    await dbContext.SaveChangesAsync(cancellationToken);
                }
            }

            var record = new UserRealName
            {
                UserId = userId,
                RealName = normalized.RealName!,
                IdCardNo = normalized.IdCardNo!,
                IsDefault = makeDefault,
                IsVerified = true,
                CreateBy = actor,
                UpdateBy = actor,
            };
            dbContext.Add(record);
            await dbContext.SaveChangesAsync(cancellationToken);
            var response = ToResponse(record);
            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return UserRealNameResult<UserRealNameResponse>.Success(response);
        }
        catch (DbUpdateException exception) when (ContainsOracleError(exception, 1))
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
            }

            return Conflict(
                "REAL_NAME_DEFAULT_CONFLICT",
                "The default real-name record changed concurrently. Please retry.");
        }
        catch (IdentityDataProtectionException exception)
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
            }

            return ProtectionFailure<UserRealNameResponse>(userId, exception);
        }
    }

    public async Task<UserRealNameResult<UserRealNameResponse>> UpdateAsync(
        long userId,
        string actor,
        long realNameId,
        UpdateUserRealNameRequest request,
        CancellationToken cancellationToken)
    {
        var normalized = Normalize(request.RealName, request.IdCardNo);
        if (!normalized.IsValid)
        {
            return Invalid(normalized.ErrorCode!, normalized.Message!);
        }

        var record = await dbContext.Set<UserRealName>()
            .SingleOrDefaultAsync(
                item => item.RealNameId == realNameId && item.UserId == userId,
                cancellationToken);
        if (record is null)
        {
            return NotFound();
        }

        UnprotectedIdentityData currentIdCard;
        try
        {
            currentIdCard = protector.Unprotect(record.IdCardNo);
        }
        catch (IdentityDataProtectionException exception)
        {
            return ProtectionFailure<UserRealNameResponse>(userId, exception, realNameId);
        }

        var identityChanged =
            !string.Equals(record.RealName, normalized.RealName, StringComparison.Ordinal) ||
            !string.Equals(currentIdCard.Value, normalized.IdCardNo, StringComparison.Ordinal);
        if (record.IsVerified && identityChanged)
        {
            return Conflict(
                "REAL_NAME_VERIFIED_IMMUTABLE",
                "Verified identity fields cannot be changed.");
        }

        if (identityChanged)
        {
            var otherRecords = await dbContext.Set<UserRealName>()
                .AsNoTracking()
                .Where(item => item.UserId == userId && item.RealNameId != realNameId)
                .ToListAsync(cancellationToken);
            try
            {
                if (ContainsIdCard(otherRecords, normalized.IdCardNo!, realNameId))
                {
                    return Conflict(
                        "REAL_NAME_DUPLICATE_ID_CARD",
                        "The identity-card number is already saved for this user.");
                }
            }
            catch (IdentityDataProtectionException exception)
            {
                return ProtectionFailure<UserRealNameResponse>(userId, exception, realNameId);
            }
        }

        record.RealName = normalized.RealName!;
        record.IdCardNo = normalized.IdCardNo!;
        record.IsVerified = true;
        record.UpdateBy = actor;
        if (currentIdCard.IsLegacy)
        {
            dbContext.Entry(record).Property(item => item.IdCardNo).IsModified = true;
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return UserRealNameResult<UserRealNameResponse>.Success(ToResponse(record));
        }
        catch (IdentityDataProtectionException exception)
        {
            return ProtectionFailure<UserRealNameResponse>(userId, exception, realNameId);
        }
    }

    public async Task<UserRealNameResult<UserRealNameResponse>> SetDefaultAsync(
        long userId,
        string actor,
        long realNameId,
        CancellationToken cancellationToken)
    {
        var records = await dbContext.Set<UserRealName>()
            .Where(item => item.UserId == userId)
            .ToListAsync(cancellationToken);
        var target = records.SingleOrDefault(item => item.RealNameId == realNameId);
        if (target is null)
        {
            return NotFound();
        }

        if (target.IsDefault)
        {
            try
            {
                return UserRealNameResult<UserRealNameResponse>.Success(ToResponse(target));
            }
            catch (IdentityDataProtectionException exception)
            {
                return ProtectionFailure<UserRealNameResponse>(userId, exception, realNameId);
            }
        }

        await using var transaction = dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;
        try
        {
            foreach (var record in records.Where(item => item.IsDefault))
            {
                record.IsDefault = false;
                record.UpdateBy = actor;
            }
            await dbContext.SaveChangesAsync(cancellationToken);

            target.IsDefault = true;
            target.UpdateBy = actor;
            await dbContext.SaveChangesAsync(cancellationToken);
            var response = ToResponse(target);
            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return UserRealNameResult<UserRealNameResponse>.Success(response);
        }
        catch (DbUpdateException exception) when (ContainsOracleError(exception, 1))
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
            }

            return Conflict(
                "REAL_NAME_DEFAULT_CONFLICT",
                "The default real-name record changed concurrently. Please retry.");
        }
        catch (IdentityDataProtectionException exception)
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
            }

            return ProtectionFailure<UserRealNameResponse>(userId, exception, realNameId);
        }
    }

    public async Task<UserRealNameResult<bool>> DeleteAsync(
        long userId,
        string actor,
        long realNameId,
        CancellationToken cancellationToken)
    {
        var record = await dbContext.Set<UserRealName>()
            .SingleOrDefaultAsync(
                item => item.RealNameId == realNameId && item.UserId == userId,
                cancellationToken);
        if (record is null)
        {
            return UserRealNameResult<bool>.Fail(
                UserRealNameFailure.NotFound,
                "REAL_NAME_NOT_FOUND",
                "The real-name record does not exist.");
        }

        var inUse = await dbContext.Set<OrderItem>()
            .AsNoTracking()
            .AnyAsync(item => item.RealNameId == realNameId, cancellationToken);
        if (inUse)
        {
            return UserRealNameResult<bool>.Fail(
                UserRealNameFailure.Conflict,
                "REAL_NAME_IN_USE",
                "The real-name record is referenced by an order and cannot be deleted.");
        }

        await using var transaction = dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;
        var wasDefault = record.IsDefault;
        dbContext.Remove(record);
        await dbContext.SaveChangesAsync(cancellationToken);

        if (wasDefault)
        {
            var replacement = await dbContext.Set<UserRealName>()
                .Where(item => item.UserId == userId)
                .OrderBy(item => item.RealNameId)
                .FirstOrDefaultAsync(cancellationToken);
            if (replacement is not null)
            {
                replacement.IsDefault = true;
                replacement.UpdateBy = actor;
                await dbContext.SaveChangesAsync(cancellationToken);
            }
        }

        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        return UserRealNameResult<bool>.Success(true);
    }

    private bool ContainsIdCard(
        IEnumerable<UserRealName> records,
        string idCardNo,
        long? exceptRealNameId) => records.Any(record =>
        record.RealNameId != exceptRealNameId &&
        string.Equals(
            protector.Unprotect(record.IdCardNo).Value,
            idCardNo,
            StringComparison.Ordinal));

    private UserRealNameResponse ToResponse(UserRealName record) => new(
        record.RealNameId,
        record.RealName,
        protector.MaskStoredValue(record.IdCardNo),
        record.IsDefault,
        record.IsVerified,
        record.CreateTime,
        record.UpdateTime);

    private UserRealNameResult<T> ProtectionFailure<T>(
        long userId,
        IdentityDataProtectionException exception,
        long? realNameId = null)
    {
        logger.LogError(
            exception,
            "Identity data could not be authenticated for user {UserId}, record {RealNameId}.",
            userId,
            realNameId);
        return UserRealNameResult<T>.Fail(
            UserRealNameFailure.Internal,
            "REAL_NAME_DATA_UNAVAILABLE",
            "The real-name data is temporarily unavailable.");
    }

    private static UserRealNameResult<UserRealNameResponse> Invalid(
        string code,
        string message) => UserRealNameResult<UserRealNameResponse>.Fail(
            UserRealNameFailure.InvalidRequest,
            code,
            message);

    private static UserRealNameResult<UserRealNameResponse> NotFound() =>
        UserRealNameResult<UserRealNameResponse>.Fail(
            UserRealNameFailure.NotFound,
            "REAL_NAME_NOT_FOUND",
            "The real-name record does not exist.");

    private static UserRealNameResult<UserRealNameResponse> Conflict(
        string code,
        string message) => UserRealNameResult<UserRealNameResponse>.Fail(
            UserRealNameFailure.Conflict,
            code,
            message);

    private static NormalizedIdentity Normalize(string realName, string idCardNo)
    {
        var normalizedName = realName?.Trim();
        if (normalizedName is null || normalizedName.Length is < 2 or > 50)
        {
            return NormalizedIdentity.Invalid(
                "REAL_NAME_INVALID_NAME",
                "RealName must contain between 2 and 50 characters.");
        }

        var normalizedIdCard = string.Concat(
                (idCardNo ?? string.Empty).Where(character => !char.IsWhiteSpace(character)))
            .ToUpperInvariant();
        if (!IdCardRegex().IsMatch(normalizedIdCard))
        {
            return NormalizedIdentity.Invalid(
                "REAL_NAME_INVALID_ID_CARD",
                "IdCardNo must contain 17 digits followed by a digit or X.");
        }

        return NormalizedIdentity.Valid(normalizedName, normalizedIdCard);
    }

    private static bool ContainsOracleError(DbUpdateException exception, int number)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is OracleException oracleException && oracleException.Number == number)
            {
                return true;
            }
        }

        return false;
    }

    [GeneratedRegex(@"^[0-9]{17}[0-9X]$")]
    private static partial Regex IdCardRegex();

    private sealed record NormalizedIdentity(
        bool IsValid,
        string? RealName,
        string? IdCardNo,
        string? ErrorCode,
        string? Message)
    {
        public static NormalizedIdentity Valid(string realName, string idCardNo) =>
            new(true, realName, idCardNo, null, null);

        public static NormalizedIdentity Invalid(string errorCode, string message) =>
            new(false, null, null, errorCode, message);
    }
}
