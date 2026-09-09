using Microsoft.EntityFrameworkCore;
using ShowtimeBackend.Common;
using ShowtimeBackend.Data;
using ShowtimeBackend.DTOs.UserPermission;
using ShowtimeBackend.Entities.OrderTicket;
using ShowtimeBackend.Entities.SeatZone;
using ShowtimeBackend.Entities.UserPermission;

namespace ShowtimeBackend.Services.UserPermission;

public sealed class AdminUserService(
    AppDbContext dbContext,
    IAuthService authService) : IAdminUserService
{
    private const int MaxPageSize = 100;

    public async Task<AdminUserResult<PagedResponse<AdminUserResponse>>> QueryAsync(
        AdminUserQueryRequest query,
        CancellationToken cancellationToken)
    {
        var pageIndex = query.PageIndex < 1 ? 1 : query.PageIndex;
        var pageSize = query.PageSize < 1
            ? 10
            : query.PageSize > MaxPageSize
                ? MaxPageSize
                : query.PageSize;
        var keyword = query.Keyword?.Trim();

        var userQuery = dbContext.Set<SysUser>()
            .AsNoTracking()
            .AsQueryable();
        if (query.Status.HasValue)
        {
            userQuery = userQuery.Where(user => user.Status == query.Status.Value);
        }

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            userQuery = userQuery.Where(
                user => user.UserName.Contains(keyword)
                    || (user.Nickname != null && user.Nickname.Contains(keyword))
                    || user.Phone.Contains(keyword)
                    || (user.Email != null && user.Email.Contains(keyword)));
        }

        var totalCount = await userQuery.CountAsync(cancellationToken);
        var users = await userQuery
            .OrderByDescending(user => user.UserId)
            .Skip((pageIndex - 1) * pageSize)
            .Take(pageSize)
            .Include(user => user.UserRoles)
            .ThenInclude(userRole => userRole.Role)
            .ToListAsync(cancellationToken);

        var items = users.Select(ToResponse).ToList();
        return AdminUserResult<PagedResponse<AdminUserResponse>>.Success(
            new PagedResponse<AdminUserResponse>(
                items,
                pageIndex,
                pageSize,
                totalCount));
    }

    public async Task<AdminUserResult<AdminUserResponse>> CreateAsync(
        RegisterRequest request,
        string operatorName,
        CancellationToken cancellationToken)
    {
        var registration = await authService.RegisterAsync(
            request,
            cancellationToken,
            operatorName);
        if (!registration.IsSuccess)
        {
            return MapRegistrationFailure(registration.Failure);
        }

        var created = await dbContext.Set<SysUser>()
            .AsNoTracking()
            .Include(user => user.UserRoles)
            .ThenInclude(userRole => userRole.Role)
            .SingleOrDefaultAsync(
                user => user.UserId == registration.Value!.User.UserId,
                cancellationToken);
        if (created is null)
        {
            return AdminUserResult<AdminUserResponse>.Fail(
                AdminUserFailure.Internal,
                "USER_CREATE_VERIFY_FAILED",
                "The user was created but could not be read back.");
        }

        return AdminUserResult<AdminUserResponse>.Success(ToResponse(created));
    }

    public async Task<AdminUserResult<bool>> DeleteAsync(
        long userId,
        string operatorName,
        CancellationToken cancellationToken)
    {
        var user = await dbContext.Set<SysUser>()
            .Include(entity => entity.UserRoles)
            .SingleOrDefaultAsync(
                entity => entity.UserId == userId,
                cancellationToken);
        if (user is null)
        {
            return AdminUserResult<bool>.Fail(
                AdminUserFailure.NotFound,
                "USER_NOT_FOUND",
                "The user does not exist.");
        }

        if (!string.IsNullOrWhiteSpace(operatorName)
            && string.Equals(user.UserName, operatorName, StringComparison.Ordinal))
        {
            return AdminUserResult<bool>.Fail(
                AdminUserFailure.Conflict,
                "USER_CANNOT_DELETE_SELF",
                "An administrator cannot delete the account currently in use.");
        }

        if (await HasRelatedDataAsync(userId, cancellationToken))
        {
            return AdminUserResult<bool>.Fail(
                AdminUserFailure.Conflict,
                "USER_HAS_RELATED_DATA",
                "The user has related orders, tickets, sessions or profile records and cannot be deleted.");
        }

        dbContext.Remove(user);
        await dbContext.SaveChangesAsync(cancellationToken);
        return AdminUserResult<bool>.Success(true);
    }

    private async Task<bool> HasRelatedDataAsync(
        long userId,
        CancellationToken cancellationToken)
    {
        // 除 USER_ROLE（ON DELETE CASCADE）外，这些表都以外键引用 SYS_USER，
        // 存在任一行都会阻止物理删除，因此删除前必须逐表探测。
        return await dbContext.Set<UserSession>()
                .AnyAsync(item => item.UserId == userId, cancellationToken)
            || await dbContext.Set<UserRealName>()
                .AnyAsync(item => item.UserId == userId, cancellationToken)
            || await dbContext.Set<UserBlacklist>()
                .AnyAsync(item => item.UserId == userId, cancellationToken)
            || await dbContext.Set<OperationLog>()
                .AnyAsync(item => item.UserId == userId, cancellationToken)
            || await dbContext.Set<Order>()
                .AnyAsync(item => item.UserId == userId, cancellationToken)
            || await dbContext.Set<Payment>()
                .AnyAsync(item => item.UserId == userId, cancellationToken)
            || await dbContext.Set<ETicket>()
                .AnyAsync(item => item.UserId == userId, cancellationToken)
            || await dbContext.Set<RefundRequest>()
                .AnyAsync(item => item.UserId == userId, cancellationToken)
            || await dbContext.Set<ExchangeRequest>()
                .AnyAsync(item => item.UserId == userId, cancellationToken)
            || await dbContext.Set<SeatLock>()
                .AnyAsync(item => item.UserId == userId, cancellationToken);
    }

    private static AdminUserResult<AdminUserResponse> MapRegistrationFailure(
        AuthFailure failure)
    {
        var (adminFailure, code, message) = failure switch
        {
            AuthFailure.UserNameTaken => (
                AdminUserFailure.Conflict,
                "AUTH_USERNAME_TAKEN",
                "The user name is already registered."),
            AuthFailure.PhoneTaken => (
                AdminUserFailure.Conflict,
                "AUTH_PHONE_TAKEN",
                "The phone number is already registered."),
            AuthFailure.EmailTaken => (
                AdminUserFailure.Conflict,
                "AUTH_EMAIL_TAKEN",
                "The email address is already registered."),
            AuthFailure.DefaultRoleUnavailable => (
                AdminUserFailure.Unavailable,
                "AUTH_DEFAULT_ROLE_UNAVAILABLE",
                "User creation is temporarily unavailable."),
            _ => throw new ArgumentOutOfRangeException(
                nameof(failure),
                failure,
                "Unknown registration failure."),
        };

        return AdminUserResult<AdminUserResponse>.Fail(
            adminFailure,
            code,
            message);
    }

    private static AdminUserResponse ToResponse(SysUser user) => new(
        user.UserId,
        user.UserName,
        user.Nickname,
        user.Phone,
        user.Email,
        user.AvatarUrl,
        user.UserType,
        user.Status,
        user.UserRoles
            .Where(userRole => userRole.Role.Status)
            .Select(userRole => userRole.Role.RoleCode)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray(),
        user.CreateTime,
        user.UpdateTime);
}
