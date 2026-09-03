using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ShowtimeBackend.Common.Jwt;
using ShowtimeBackend.Data;
using ShowtimeBackend.DTOs.UserPermission;
using ShowtimeBackend.Entities.UserPermission;

namespace ShowtimeBackend.Services.UserPermission;

public sealed class UserSessionService(
    AppDbContext dbContext,
    IRefreshTokenService refreshTokenService,
    IOptions<JwtOptions> jwtOptions,
    IOperationLogWriter operationLogWriter,
    TimeProvider timeProvider) : IUserSessionService
{
    private readonly JwtOptions _jwtOptions = jwtOptions.Value;

    public async Task<UserSessionResult<SessionIssueData>> StartAsync(
        long userId,
        ClientRequestMetadata client,
        CancellationToken cancellationToken)
    {
        var now = UtcNow();
        var actor = userId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            cancellationToken);

        var lockedUsers = await dbContext.Set<SysUser>()
            .Where(user => user.UserId == userId)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    user => user.UpdateTime,
                    user => user.UpdateTime),
                cancellationToken);
        if (lockedUsers != 1)
        {
            await transaction.RollbackAsync(cancellationToken);
            return UserSessionResult<SessionIssueData>.Failed(
                UserSessionFailure.UserNotFound);
        }

        var activeSessions = await dbContext.Set<UserSession>()
            .Where(session => session.UserId == userId
                && session.Status == UserSessionStatuses.Active)
            .OrderByDescending(session => session.LoginTime)
            .ThenByDescending(session => session.UserSessionId)
            .ToListAsync(cancellationToken);

        var normalizedClient = new ClientRequestMetadata(
            Normalize(client.IpAddress, 50),
            Normalize(client.UserAgent, 500));
        var previousSession = activeSessions.FirstOrDefault();
        var riskDetected = previousSession is not null
            && (!string.Equals(
                    previousSession.IpAddress,
                    normalizedClient.IpAddress,
                    StringComparison.Ordinal)
                || !string.Equals(
                    previousSession.UserAgent,
                    normalizedClient.UserAgent,
                    StringComparison.Ordinal));

        foreach (var session in activeSessions)
        {
            session.Status = riskDetected
                ? UserSessionStatuses.Locked
                : UserSessionStatuses.Logout;
            session.RiskFlag |= riskDetected;
            session.LogoutTime = now;
            session.UpdateTime = now;
            session.UpdateBy = actor;
        }

        var sessionEntity = new UserSession
        {
            UserId = userId,
            SessionToken = Convert.ToHexString(
                RandomNumberGenerator.GetBytes(32)),
            LoginTime = now,
            ExpireTime = now.AddDays(_jwtOptions.RefreshTokenExpirationDays),
            IpAddress = normalizedClient.IpAddress,
            UserAgent = normalizedClient.UserAgent,
            RiskFlag = false,
            Status = UserSessionStatuses.Active,
            CreateTime = now,
            UpdateTime = now,
            CreateBy = actor,
            UpdateBy = actor,
        };
        dbContext.Set<UserSession>().Add(sessionEntity);
        await dbContext.SaveChangesAsync(cancellationToken);

        var issuedToken = refreshTokenService.Issue(
            sessionEntity.UserSessionId,
            sessionEntity.ExpireTime);
        sessionEntity.SessionToken = issuedToken.TokenHash;
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return UserSessionResult<SessionIssueData>.Succeeded(
            new SessionIssueData(
                sessionEntity.UserSessionId,
                issuedToken.RawToken,
                issuedToken.ExpiresAtUtc,
                riskDetected));
    }

    public async Task<UserSessionResult<SessionRefreshData>> RotateAsync(
        string refreshToken,
        CancellationToken cancellationToken)
    {
        if (!refreshTokenService.TryParseAndVerify(
                refreshToken,
                out var parsedToken))
        {
            return UserSessionResult<SessionRefreshData>.Failed(
                UserSessionFailure.InvalidToken);
        }

        var parsed = parsedToken!;
        var now = UtcNow();
        var expiresAt = now.AddDays(_jwtOptions.RefreshTokenExpirationDays);
        var nextToken = refreshTokenService.Issue(parsed.SessionId, expiresAt);

        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            cancellationToken);
        var updated = await dbContext.Set<UserSession>()
            .Where(session => session.UserSessionId == parsed.SessionId
                && session.SessionToken == parsed.TokenHash
                && session.Status == UserSessionStatuses.Active
                && session.ExpireTime > now)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(
                        session => session.SessionToken,
                        nextToken.TokenHash)
                    .SetProperty(session => session.ExpireTime, expiresAt)
                    .SetProperty(session => session.UpdateTime, now)
                    .SetProperty(session => session.UpdateBy, "refresh"),
                cancellationToken);

        if (updated == 1)
        {
            var userId = await dbContext.Set<UserSession>()
                .AsNoTracking()
                .Where(session => session.UserSessionId == parsed.SessionId)
                .Select(session => session.UserId)
                .SingleAsync(cancellationToken);
            var user = await dbContext.Set<SysUser>()
                .AsNoTracking()
                .Include(user => user.UserRoles)
                .ThenInclude(userRole => userRole.Role)
                .SingleOrDefaultAsync(
                    candidate => candidate.UserId == userId,
                    cancellationToken);

            if (user is null || user.Status != 1)
            {
                await LockSessionAsync(parsed.SessionId, now, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return UserSessionResult<SessionRefreshData>.Failed(
                    UserSessionFailure.AccountUnavailable);
            }

            var roleCodes = user.UserRoles
                .Where(userRole => userRole.Role.Status)
                .Select(userRole => userRole.Role.RoleCode)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray();

            await transaction.CommitAsync(cancellationToken);
            return UserSessionResult<SessionRefreshData>.Succeeded(
                new SessionRefreshData(
                    parsed.SessionId,
                    nextToken.RawToken,
                    nextToken.ExpiresAtUtc,
                    user,
                    roleCodes));
        }

        var sessionState = await dbContext.Set<UserSession>()
            .AsNoTracking()
            .SingleOrDefaultAsync(
                session => session.UserSessionId == parsed.SessionId,
                cancellationToken);
        if (sessionState is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return UserSessionResult<SessionRefreshData>.Failed(
                UserSessionFailure.InvalidToken);
        }

        if (sessionState.Status == UserSessionStatuses.Active
            && sessionState.ExpireTime <= now)
        {
            await dbContext.Set<UserSession>()
                .Where(session => session.UserSessionId == parsed.SessionId
                    && session.Status == UserSessionStatuses.Active)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(
                            session => session.Status,
                            UserSessionStatuses.Expired)
                        .SetProperty(session => session.UpdateTime, now)
                        .SetProperty(session => session.UpdateBy, "expiration"),
                    cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return UserSessionResult<SessionRefreshData>.Failed(
                UserSessionFailure.Expired);
        }

        if (sessionState.Status == UserSessionStatuses.Active
            && !refreshTokenService.FixedTimeEquals(
                sessionState.SessionToken,
                parsed.TokenHash))
        {
            await LockSessionAsync(parsed.SessionId, now, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            await operationLogWriter.WriteBestEffortAsync(
                new OperationLogWriteRequest(
                    Module: "AUTH",
                    OperationType: "REFRESH_TOKEN_REUSE",
                    Succeeded: false,
                    UserId: sessionState.UserId,
                    RequestSummary: new { SessionId = parsed.SessionId },
                    ResponseSummary: new { ResultCode = "AUTH_REFRESH_TOKEN_REUSED" },
                    ErrorMessage: "AUTH_REFRESH_TOKEN_REUSED"),
                cancellationToken);
            return UserSessionResult<SessionRefreshData>.Failed(
                UserSessionFailure.TokenReused);
        }

        await transaction.RollbackAsync(cancellationToken);
        return UserSessionResult<SessionRefreshData>.Failed(
            sessionState.Status switch
            {
                UserSessionStatuses.Expired => UserSessionFailure.Expired,
                UserSessionStatuses.Logout => UserSessionFailure.LoggedOut,
                UserSessionStatuses.Locked => UserSessionFailure.Locked,
                _ => UserSessionFailure.InvalidToken,
            });
    }

    public async Task<bool> IsActiveAsync(
        long userId,
        long sessionId,
        CancellationToken cancellationToken)
    {
        var now = UtcNow();
        var session = await dbContext.Set<UserSession>()
            .AsNoTracking()
            .Where(candidate => candidate.UserSessionId == sessionId
                && candidate.UserId == userId)
            .Select(candidate => new
            {
                candidate.Status,
                candidate.ExpireTime,
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (session is null || session.Status != UserSessionStatuses.Active)
        {
            return false;
        }

        if (session.ExpireTime > now)
        {
            return true;
        }

        await dbContext.Set<UserSession>()
            .Where(candidate => candidate.UserSessionId == sessionId
                && candidate.UserId == userId
                && candidate.Status == UserSessionStatuses.Active)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(
                        candidate => candidate.Status,
                        UserSessionStatuses.Expired)
                    .SetProperty(candidate => candidate.UpdateTime, now)
                    .SetProperty(candidate => candidate.UpdateBy, "expiration"),
                cancellationToken);
        return false;
    }

    public Task<int> LogoutCurrentAsync(
        long userId,
        long sessionId,
        CancellationToken cancellationToken) =>
        LogoutWhereAsync(
            userId,
            session => session.UserSessionId == sessionId,
            cancellationToken);

    public Task<int> LogoutAllAsync(
        long userId,
        CancellationToken cancellationToken) =>
        LogoutWhereAsync(userId, _ => true, cancellationToken);

    public async Task<IReadOnlyList<UserSessionResponse>> ListAsync(
        long userId,
        long currentSessionId,
        CancellationToken cancellationToken)
    {
        var now = UtcNow();
        await dbContext.Set<UserSession>()
            .Where(session => session.UserId == userId
                && session.Status == UserSessionStatuses.Active
                && session.ExpireTime <= now)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(
                        session => session.Status,
                        UserSessionStatuses.Expired)
                    .SetProperty(session => session.UpdateTime, now)
                    .SetProperty(session => session.UpdateBy, "expiration"),
                cancellationToken);

        return await dbContext.Set<UserSession>()
            .AsNoTracking()
            .Where(session => session.UserId == userId)
            .OrderByDescending(session => session.LoginTime)
            .ThenByDescending(session => session.UserSessionId)
            .Select(session => new UserSessionResponse(
                session.UserSessionId,
                session.LoginTime,
                session.ExpireTime,
                session.LogoutTime,
                session.IpAddress,
                session.UserAgent,
                session.RiskFlag,
                session.Status,
                session.UserSessionId == currentSessionId))
            .ToListAsync(cancellationToken);
    }

    public async Task<UserSessionResult<int>> RevokeAsync(
        long userId,
        long targetSessionId,
        CancellationToken cancellationToken)
    {
        var owned = await dbContext.Set<UserSession>()
            .AsNoTracking()
            .AnyAsync(
                session => session.UserSessionId == targetSessionId
                    && session.UserId == userId,
                cancellationToken);
        if (!owned)
        {
            return UserSessionResult<int>.Failed(UserSessionFailure.NotFound);
        }

        var revoked = await LogoutWhereAsync(
            userId,
            session => session.UserSessionId == targetSessionId,
            cancellationToken);
        return UserSessionResult<int>.Succeeded(revoked);
    }

    private async Task<int> LogoutWhereAsync(
        long userId,
        System.Linq.Expressions.Expression<Func<UserSession, bool>> predicate,
        CancellationToken cancellationToken)
    {
        var now = UtcNow();
        return await dbContext.Set<UserSession>()
            .Where(session => session.UserId == userId
                && session.Status == UserSessionStatuses.Active)
            .Where(predicate)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(
                        session => session.Status,
                        UserSessionStatuses.Logout)
                    .SetProperty(session => session.LogoutTime, now)
                    .SetProperty(session => session.UpdateTime, now)
                    .SetProperty(
                        session => session.UpdateBy,
                        userId.ToString()),
                cancellationToken);
    }

    private Task<int> LockSessionAsync(
        long sessionId,
        DateTime now,
        CancellationToken cancellationToken) =>
        dbContext.Set<UserSession>()
            .Where(session => session.UserSessionId == sessionId
                && session.Status == UserSessionStatuses.Active)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(
                        session => session.Status,
                        UserSessionStatuses.Locked)
                    .SetProperty(session => session.RiskFlag, true)
                    .SetProperty(session => session.LogoutTime, now)
                    .SetProperty(session => session.UpdateTime, now)
                    .SetProperty(session => session.UpdateBy, "security"),
                cancellationToken);

    private DateTime UtcNow() => timeProvider.GetUtcNow().UtcDateTime;

    private static string? Normalize(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = string.Concat(value.Select(character =>
            char.IsControl(character) ? ' ' : character)).Trim();

        return normalized.Length <= maxLength
            ? normalized
            : normalized[..maxLength];
    }
}
