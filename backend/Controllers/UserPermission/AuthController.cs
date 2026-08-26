using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShowtimeBackend.Common;
using ShowtimeBackend.DTOs.UserPermission;
using ShowtimeBackend.Services.UserPermission;

namespace ShowtimeBackend.Controllers.UserPermission;

[ApiController]
[Route("api/auth")]
[AllowAnonymous]
public sealed class AuthController(
    IAuthService authService,
    IOperationLogWriter operationLogWriter,
    TimeProvider timeProvider) : ControllerBase
{
    [HttpPost("register")]
    [ProducesResponseType(typeof(ApiResponse<RegisterResponse>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse<RegisterResponse>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<RegisterResponse>), StatusCodes.Status409Conflict)]
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
    [ProducesResponseType(typeof(ApiResponse<LoginResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<LoginResponse>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<LoginResponse>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<LoginResponse>), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<LoginResponse>>> Login(
        LoginRequest request,
        CancellationToken cancellationToken)
    {
        var startedAt = timeProvider.GetTimestamp();
        var result = await authService.LoginAsync(request, cancellationToken);
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
            _ => throw new ArgumentOutOfRangeException(
                nameof(failure),
                failure,
                "Unknown authentication failure."),
        };

        return StatusCode(
            statusCode,
            ApiResponse<T>.Fail(code, message));
    }
}
