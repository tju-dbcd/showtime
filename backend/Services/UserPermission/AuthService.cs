using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ShowtimeBackend.Common.Jwt;
using ShowtimeBackend.Data;
using ShowtimeBackend.DTOs.UserPermission;
using ShowtimeBackend.Entities.UserPermission;

namespace ShowtimeBackend.Services.UserPermission;

public sealed partial class AuthService(
    AppDbContext dbContext,
    IPasswordHasher<SysUser> passwordHasher,
    IJwtTokenService jwtTokenService,
    TimeProvider timeProvider,
    ILogger<AuthService> logger) : IAuthService
{
    private const string DefaultRoleCode = "USER";

    public async Task<AuthServiceResult<RegisterResponse>> RegisterAsync(
        RegisterRequest request,
        CancellationToken cancellationToken)
    {
        var userName = request.UserName.Trim();
        var phone = request.Phone.Trim();
        var email = NormalizeOptionalEmail(request.Email);
        var nickname = NormalizeOptionalText(request.Nickname);

        var conflict = await FindRegistrationConflictAsync(
            userName,
            phone,
            email,
            cancellationToken);
        if (conflict != AuthFailure.None)
        {
            return AuthServiceResult<RegisterResponse>.Failed(conflict);
        }

        var defaultRole = await dbContext.Set<Role>()
            .AsNoTracking()
            .SingleOrDefaultAsync(
                role => role.RoleCode == DefaultRoleCode && role.Status,
                cancellationToken);
        if (defaultRole is null)
        {
            return AuthServiceResult<RegisterResponse>.Failed(
                AuthFailure.DefaultRoleUnavailable);
        }

        var user = new SysUser
        {
            UserName = userName,
            PasswordHash = string.Empty,
            Nickname = nickname,
            Phone = phone,
            Email = email,
            UserType = "NORMAL",
            Status = 1,
            CreateBy = userName,
            UpdateBy = userName,
        };
        user.PasswordHash = passwordHasher.HashPassword(user, request.Password);
        user.UserRoles.Add(new UserRole { RoleId = defaultRole.RoleId });

        dbContext.Add(user);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            logger.LogInformation(
                "Registration persistence failed for user name {UserName}; classifying the database conflict.",
                userName);

            dbContext.ChangeTracker.Clear();

            conflict = await FindRegistrationConflictAsync(
                userName,
                phone,
                email,
                cancellationToken);
            if (conflict != AuthFailure.None)
            {
                return AuthServiceResult<RegisterResponse>.Failed(conflict);
            }

            var defaultRoleStillAvailable = await dbContext.Set<Role>()
                .AsNoTracking()
                .CountAsync(
                    role => role.RoleCode == DefaultRoleCode && role.Status,
                    cancellationToken) > 0;
            if (!defaultRoleStillAvailable)
            {
                return AuthServiceResult<RegisterResponse>.Failed(
                    AuthFailure.DefaultRoleUnavailable);
            }

            throw;
        }

        var response = new RegisterResponse(
            CreateUserResponse(user, [DefaultRoleCode]));

        return AuthServiceResult<RegisterResponse>.Succeeded(response);
    }

    public async Task<AuthServiceResult<LoginResponse>> LoginAsync(
        LoginRequest request,
        CancellationToken cancellationToken)
    {
        var account = request.Account.Trim();
        var userQuery = dbContext.Set<SysUser>()
            .AsNoTracking()
            .Include(user => user.UserRoles)
            .ThenInclude(userRole => userRole.Role)
            .AsQueryable();

        if (account.Contains('@'))
        {
            var email = account.ToLowerInvariant();
            userQuery = userQuery.Where(
                user => user.Email != null && user.Email.ToLower() == email);
        }
        else if (PhoneRegex().IsMatch(account))
        {
            userQuery = userQuery.Where(user => user.Phone == account);
        }
        else
        {
            userQuery = userQuery.Where(user => user.UserName == account);
        }

        var matches = await userQuery
            .OrderBy(user => user.UserId)
            .Take(2)
            .ToListAsync(cancellationToken);

        if (matches.Count != 1)
        {
            return AuthServiceResult<LoginResponse>.Failed(
                AuthFailure.InvalidCredentials);
        }

        var user = matches[0];
        var passwordResult = passwordHasher.VerifyHashedPassword(
            user,
            user.PasswordHash,
            request.Password);
        if (passwordResult == PasswordVerificationResult.Failed)
        {
            return AuthServiceResult<LoginResponse>.Failed(
                AuthFailure.InvalidCredentials);
        }

        if (user.Status == 0)
        {
            return AuthServiceResult<LoginResponse>.Failed(
                AuthFailure.AccountDisabled);
        }

        if (user.Status == 2)
        {
            return AuthServiceResult<LoginResponse>.Failed(
                AuthFailure.AccountLocked);
        }

        if (user.Status != 1)
        {
            return AuthServiceResult<LoginResponse>.Failed(
                AuthFailure.AccountDisabled);
        }

        var roleCodes = user.UserRoles
            .Where(userRole => userRole.Role.Status)
            .Select(userRole => userRole.Role.RoleCode)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        var token = jwtTokenService.CreateToken(user, roleCodes);
        var expiresIn = Math.Max(
            0,
            (long)Math.Ceiling(
                (token.ExpiresAtUtc - timeProvider.GetUtcNow().UtcDateTime).TotalSeconds));

        var response = new LoginResponse(
            token.AccessToken,
            "Bearer",
            expiresIn,
            token.ExpiresAtUtc,
            CreateUserResponse(user, roleCodes));

        return AuthServiceResult<LoginResponse>.Succeeded(response);
    }

    private async Task<AuthFailure> FindRegistrationConflictAsync(
        string userName,
        string phone,
        string? email,
        CancellationToken cancellationToken)
    {
        if (await dbContext.Set<SysUser>()
            .AsNoTracking()
            .CountAsync(user => user.UserName == userName, cancellationToken) > 0)
        {
            return AuthFailure.UserNameTaken;
        }

        if (await dbContext.Set<SysUser>()
            .AsNoTracking()
            .CountAsync(user => user.Phone == phone, cancellationToken) > 0)
        {
            return AuthFailure.PhoneTaken;
        }

        if (email is not null && await dbContext.Set<SysUser>()
            .AsNoTracking()
            .CountAsync(
                user => user.Email != null && user.Email.ToLower() == email,
                cancellationToken) > 0)
        {
            return AuthFailure.EmailTaken;
        }

        return AuthFailure.None;
    }

    private static UserResponse CreateUserResponse(
        SysUser user,
        IReadOnlyList<string> roleCodes) =>
        new(
            user.UserId,
            user.UserName,
            user.Nickname,
            user.Phone,
            user.Email,
            roleCodes);

    private static string? NormalizeOptionalEmail(string? value)
    {
        var normalized = NormalizeOptionalText(value);
        return normalized?.ToLowerInvariant();
    }

    private static string? NormalizeOptionalText(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    [GeneratedRegex(@"^(?:\+?[0-9]{6,19}|[0-9]{20})$")]
    private static partial Regex PhoneRegex();
}
