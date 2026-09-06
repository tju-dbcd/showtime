using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ShowtimeBackend.Common;
using ShowtimeBackend.Common.RateLimiting;
using ShowtimeBackend.DTOs.UserPermission;
using ShowtimeBackend.Services.UserPermission;

namespace ShowtimeBackend.Controllers.UserPermission;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(
    IAuthService authService,
    IUserSessionService userSessionService,
    IOperationLogWriter operationLogWriter,
    TimeProvider timeProvider) : ControllerBase
{
    [HttpPost("register")]
    [AllowAnonymous]
    [EnableRateLimiting(ApiRateLimitPolicyNames.Register)]
    [ProducesResponseType(typeof(ApiResponse<RegisterResponse>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse<RegisterResponse>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<RegisterResponse>), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse<RegisterResponse>), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(ApiResponse<RegisterResponse>), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<ApiResponse<RegisterResponse>>> Register(
        RegisterRequest request,
        CancellationToken cancellationToken)
    {
        var result = await authService.RegisterAsync(request, cancellationToken);
        if (result.IsSuccess)
        {
            return StatusCode(
                StatusCodes.Status201Created,
                ApiResponse<RegisterResponse>.Ok(
                    result.Value!,
                    "Registration succeeded."));
        }

        return CreateFailure<RegisterResponse>(result.Failure);
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(ApiRateLimitPolicyNames.Login)]
    [ProducesResponseType(typeof(ApiResponse<LoginResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<LoginResponse>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<LoginResponse>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<LoginResponse>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<LoginResponse>), StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<ApiResponse<LoginResponse>>> Login(
        LoginRequest request,
        CancellationToken cancellationToken)
    {
        var startedAt = timeProvider.GetTimestamp();
        var result = await authService.LoginAsync(
            request,
            GetClientMetadata(),
            cancellationToken);
        var costTime = ToMilliseconds(timeProvider.GetElapsedTime(startedAt));
        if (result.IsSuccess)
        {
            await operationLogWriter.WriteBestEffortAsync(
                new OperationLogWriteRequest(
                    Module: "AUTH",
                    OperationType: "LOGIN",
                    Succeeded: true,
                    UserId: result.Value!.User.UserId,
                    UserName: result.Value.User.UserName,
                    CostTimeMilliseconds: costTime,
                    RequestSummary: new { AccountType = GetAccountType(request.Account) },
                    ResponseSummary: new { ResultCode = "SUCCESS" }),
                cancellationToken);
            return Ok(
                ApiResponse<LoginResponse>.Ok(
                    result.Value!,
                    "Login succeeded."));
        }

        await operationLogWriter.WriteBestEffortAsync(
            new OperationLogWriteRequest(
                Module: "AUTH",
                OperationType: "LOGIN",
                Succeeded: false,
                CostTimeMilliseconds: costTime,
                RequestSummary: new { AccountType = GetAccountType(request.Account) },
                ResponseSummary: new { ResultCode = ToLogCode(result.Failure) },
                ErrorMessage: ToLogCode(result.Failure)),
            cancellationToken);

        return CreateFailure<LoginResponse>(result.Failure);
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    [EnableRateLimiting(ApiRateLimitPolicyNames.Refresh)]
    [ProducesResponseType(typeof(ApiResponse<RefreshTokenResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<RefreshTokenResponse>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<RefreshTokenResponse>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<RefreshTokenResponse>), StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<ApiResponse<RefreshTokenResponse>>> Refresh(
        RefreshTokenRequest request,
        CancellationToken cancellationToken)
    {
        var startedAt = timeProvider.GetTimestamp();
        var result = await authService.RefreshAsync(request, cancellationToken);
        await WriteSecurityAuditAsync(
            "REFRESH_TOKEN",
            result.IsSuccess,
            null,
            result.IsSuccess ? "SUCCESS" : ToLogCode(result.Failure),
            ToMilliseconds(timeProvider.GetElapsedTime(startedAt)),
            cancellationToken);

        return result.IsSuccess
            ? Ok(ApiResponse<RefreshTokenResponse>.Ok(
                result.Value!,
                "Token refreshed."))
            : CreateFailure<RefreshTokenResponse>(result.Failure);
    }

    [HttpPost("logout")]
    [Authorize]
    [ProducesResponseType(typeof(ApiResponse<SessionRevocationResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<SessionRevocationResponse>), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ApiResponse<SessionRevocationResponse>>> Logout(
        CancellationToken cancellationToken)
    {
        if (!TryGetIdentity(out var userId, out var sessionId))
        {
            return Unauthorized(ApiResponse<SessionRevocationResponse>.Fail(
                "AUTH_REQUIRED",
                "A valid user session is required."));
        }

        var revoked = await userSessionService.LogoutCurrentAsync(
            userId,
            sessionId,
            cancellationToken);
        await WriteSecurityAuditAsync(
            "LOGOUT",
            true,
            userId,
            "SUCCESS",
            null,
            cancellationToken,
            sessionId,
            revoked);
        return Ok(ApiResponse<SessionRevocationResponse>.Ok(
            new SessionRevocationResponse(revoked),
            "Current session logged out."));
    }

    [HttpPost("logout-all")]
    [Authorize]
    [ProducesResponseType(typeof(ApiResponse<SessionRevocationResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<SessionRevocationResponse>), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ApiResponse<SessionRevocationResponse>>> LogoutAll(
        CancellationToken cancellationToken)
    {
        if (!TryGetIdentity(out var userId, out var sessionId))
        {
            return Unauthorized(ApiResponse<SessionRevocationResponse>.Fail(
                "AUTH_REQUIRED",
                "A valid user session is required."));
        }

        var revoked = await userSessionService.LogoutAllAsync(
            userId,
            cancellationToken);
        await WriteSecurityAuditAsync(
            "LOGOUT_ALL",
            true,
            userId,
            "SUCCESS",
            null,
            cancellationToken,
            sessionId,
            revoked);
        return Ok(ApiResponse<SessionRevocationResponse>.Ok(
            new SessionRevocationResponse(revoked),
            "All sessions logged out."));
    }

    [HttpGet("sessions")]
    [Authorize]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<UserSessionResponse>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<UserSessionResponse>>), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<UserSessionResponse>>>> Sessions(
        CancellationToken cancellationToken)
    {
        if (!TryGetIdentity(out var userId, out var sessionId))
        {
            return Unauthorized(ApiResponse<IReadOnlyList<UserSessionResponse>>.Fail(
                "AUTH_REQUIRED",
                "A valid user session is required."));
        }

        var sessions = await userSessionService.ListAsync(
            userId,
            sessionId,
            cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<UserSessionResponse>>.Ok(
            sessions,
            "Sessions retrieved."));
    }

    [HttpDelete("sessions/{sessionId:long}")]
    [Authorize]
    [ProducesResponseType(typeof(ApiResponse<SessionRevocationResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<SessionRevocationResponse>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<SessionRevocationResponse>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<SessionRevocationResponse>>> RevokeSession(
        long sessionId,
        CancellationToken cancellationToken)
    {
        if (!TryGetIdentity(out var userId, out var currentSessionId))
        {
            return Unauthorized(ApiResponse<SessionRevocationResponse>.Fail(
                "AUTH_REQUIRED",
                "A valid user session is required."));
        }

        var result = await userSessionService.RevokeAsync(
            userId,
            sessionId,
            cancellationToken);
        if (!result.IsSuccess)
        {
            await WriteSecurityAuditAsync(
                "SESSION_REVOKE",
                false,
                userId,
                "AUTH_SESSION_NOT_FOUND",
                null,
                cancellationToken,
                sessionId,
                0);
            return NotFound(ApiResponse<SessionRevocationResponse>.Fail(
                "AUTH_SESSION_NOT_FOUND",
                "The session was not found."));
        }

        await WriteSecurityAuditAsync(
            "SESSION_REVOKE",
            true,
            userId,
            "SUCCESS",
            null,
            cancellationToken,
            sessionId,
            result.Value);
        return Ok(ApiResponse<SessionRevocationResponse>.Ok(
            new SessionRevocationResponse(result.Value),
            "Session revoked."));
    }

    private static string GetAccountType(string account)
    {
        var normalized = account.Trim();
        if (normalized.Contains('@'))
        {
            return "EMAIL";
        }

        return normalized.All(character => character is '+' or >= '0' and <= '9')
            ? "PHONE"
            : "USERNAME";
    }

    private static string ToLogCode(AuthFailure failure) => failure switch
    {
        AuthFailure.InvalidCredentials => "AUTH_INVALID_CREDENTIALS",
        AuthFailure.AccountDisabled => "AUTH_ACCOUNT_DISABLED",
        AuthFailure.AccountLocked => "AUTH_ACCOUNT_LOCKED",
        AuthFailure.InvalidRefreshToken => "AUTH_REFRESH_INVALID",
        AuthFailure.RefreshTokenExpired => "AUTH_REFRESH_EXPIRED",
        AuthFailure.RefreshTokenLoggedOut => "AUTH_SESSION_LOGGED_OUT",
        AuthFailure.RefreshTokenLocked => "AUTH_SESSION_LOCKED",
        AuthFailure.RefreshTokenReused => "AUTH_REFRESH_TOKEN_REUSED",
        AuthFailure.SessionUnavailable => "AUTH_SESSION_UNAVAILABLE",
        _ => "AUTH_LOGIN_FAILED",
    };

    private static long ToMilliseconds(TimeSpan elapsed) =>
        Math.Max(0, (long)Math.Ceiling(elapsed.TotalMilliseconds));

    private ActionResult<ApiResponse<T>> CreateFailure<T>(AuthFailure failure)
    {
        var (statusCode, code, message) = failure switch
        {
            AuthFailure.UserNameTaken => (
                StatusCodes.Status409Conflict,
                "AUTH_USERNAME_TAKEN",
                "The user name is already registered."),
            AuthFailure.PhoneTaken => (
                StatusCodes.Status409Conflict,
                "AUTH_PHONE_TAKEN",
                "The phone number is already registered."),
            AuthFailure.EmailTaken => (
                StatusCodes.Status409Conflict,
                "AUTH_EMAIL_TAKEN",
                "The email address is already registered."),
            AuthFailure.InvalidCredentials => (
                StatusCodes.Status401Unauthorized,
                "AUTH_INVALID_CREDENTIALS",
                "The account or password is incorrect."),
            AuthFailure.AccountDisabled => (
                StatusCodes.Status403Forbidden,
                "AUTH_ACCOUNT_DISABLED",
                "The account is disabled."),
            AuthFailure.AccountLocked => (
                StatusCodes.Status403Forbidden,
                "AUTH_ACCOUNT_LOCKED",
                "The account is locked."),
            AuthFailure.DefaultRoleUnavailable => (
                StatusCodes.Status503ServiceUnavailable,
                "AUTH_DEFAULT_ROLE_UNAVAILABLE",
                "Registration is temporarily unavailable."),
            AuthFailure.SessionUnavailable => (
                StatusCodes.Status503ServiceUnavailable,
                "AUTH_SESSION_UNAVAILABLE",
                "The login session could not be created."),
            AuthFailure.InvalidRefreshToken => (
                StatusCodes.Status401Unauthorized,
                "AUTH_REFRESH_INVALID",
                "The refresh token is invalid."),
            AuthFailure.RefreshTokenExpired => (
                StatusCodes.Status401Unauthorized,
                "AUTH_REFRESH_EXPIRED",
                "The refresh token has expired."),
            AuthFailure.RefreshTokenLoggedOut => (
                StatusCodes.Status401Unauthorized,
                "AUTH_SESSION_LOGGED_OUT",
                "The session has been logged out."),
            AuthFailure.RefreshTokenLocked => (
                StatusCodes.Status401Unauthorized,
                "AUTH_SESSION_LOCKED",
                "The session has been locked."),
            AuthFailure.RefreshTokenReused => (
                StatusCodes.Status401Unauthorized,
                "AUTH_REFRESH_TOKEN_REUSED",
                "Refresh token reuse was detected; the session is locked."),
            _ => throw new ArgumentOutOfRangeException(
                nameof(failure),
                failure,
                "Unknown authentication failure."),
        };

        return StatusCode(
            statusCode,
            ApiResponse<T>.Fail(code, message));
    }

    private ClientRequestMetadata GetClientMetadata() => new(
        HttpContext.Connection.RemoteIpAddress?.ToString(),
        Request.Headers.UserAgent.ToString());

    private bool TryGetIdentity(out long userId, out long sessionId)
    {
        userId = 0;
        sessionId = 0;
        return long.TryParse(
                User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out userId)
            && userId > 0
            && long.TryParse(
                User.FindFirst("sid")?.Value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out sessionId)
            && sessionId > 0;
    }

    private ValueTask WriteSecurityAuditAsync(
        string operationType,
        bool succeeded,
        long? userId,
        string resultCode,
        long? costTime,
        CancellationToken cancellationToken,
        long? sessionId = null,
        int? revokedCount = null) =>
        operationLogWriter.WriteBestEffortAsync(
            new OperationLogWriteRequest(
                Module: "AUTH",
                OperationType: operationType,
                Succeeded: succeeded,
                UserId: userId,
                UserName: User.Identity?.Name,
                CostTimeMilliseconds: costTime,
                RequestSummary: sessionId is null
                    ? null
                    : new { SessionId = sessionId },
                ResponseSummary: new { ResultCode = resultCode, RevokedCount = revokedCount },
                ErrorMessage: succeeded ? null : resultCode),
            cancellationToken);
}
