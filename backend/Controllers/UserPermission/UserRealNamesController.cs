using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShowtimeBackend.Common;
using ShowtimeBackend.DTOs.UserPermission;
using ShowtimeBackend.Services.UserPermission;

namespace ShowtimeBackend.Controllers.UserPermission;

[ApiController]
[Authorize]
[Route("api/users/me/real-names")]
[Tags("User Real Names")]
public sealed class UserRealNamesController(
    IUserRealNameService realNameService) : UserPermissionControllerBase
{
    [HttpGet]
    [ProducesResponseType(
        typeof(ApiResponse<IReadOnlyList<UserRealNameResponse>>),
        StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<UserRealNameResponse>>>> List(
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUser(out var userId, out _))
        {
            return UnauthorizedResponse<IReadOnlyList<UserRealNameResponse>>();
        }

        var result = await realNameService.ListAsync(userId, cancellationToken);
        return result.IsSuccess
            ? Ok(ApiResponse<IReadOnlyList<UserRealNameResponse>>.Ok(
                result.Value!,
                "Real-name records retrieved."))
            : FailureResponse(result);
    }

    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<UserRealNameResponse>), StatusCodes.Status201Created)]
    public async Task<ActionResult<ApiResponse<UserRealNameResponse>>> Create(
        CreateUserRealNameRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUser(out var userId, out var actor))
        {
            return UnauthorizedResponse<UserRealNameResponse>();
        }

        var result = await realNameService.CreateAsync(
            userId,
            actor,
            request,
            cancellationToken);
        return result.IsSuccess
            ? StatusCode(
                StatusCodes.Status201Created,
                ApiResponse<UserRealNameResponse>.Ok(
                    result.Value!,
                    "Real-name record created."))
            : FailureResponse(result);
    }

    [HttpPut("{realNameId:long}")]
    [ProducesResponseType(typeof(ApiResponse<UserRealNameResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<UserRealNameResponse>>> Update(
        long realNameId,
        UpdateUserRealNameRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUser(out var userId, out var actor))
        {
            return UnauthorizedResponse<UserRealNameResponse>();
        }

        var result = await realNameService.UpdateAsync(
            userId,
            actor,
            realNameId,
            request,
            cancellationToken);
        return result.IsSuccess
            ? Ok(ApiResponse<UserRealNameResponse>.Ok(
                result.Value!,
                "Real-name record updated."))
            : FailureResponse(result);
    }

    [HttpPatch("{realNameId:long}/default")]
    [ProducesResponseType(typeof(ApiResponse<UserRealNameResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<UserRealNameResponse>>> SetDefault(
        long realNameId,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUser(out var userId, out var actor))
        {
            return UnauthorizedResponse<UserRealNameResponse>();
        }

        var result = await realNameService.SetDefaultAsync(
            userId,
            actor,
            realNameId,
            cancellationToken);
        return result.IsSuccess
            ? Ok(ApiResponse<UserRealNameResponse>.Ok(
                result.Value!,
                "Default real-name record updated."))
            : FailureResponse(result);
    }

    [HttpDelete("{realNameId:long}")]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<bool>>> Delete(
        long realNameId,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUser(out var userId, out var actor))
        {
            return UnauthorizedResponse<bool>();
        }

        var result = await realNameService.DeleteAsync(
            userId,
            actor,
            realNameId,
            cancellationToken);
        return result.IsSuccess
            ? Ok(ApiResponse<bool>.Ok(true, "Real-name record deleted."))
            : FailureResponse(result);
    }

    private ActionResult<ApiResponse<T>> FailureResponse<T>(UserRealNameResult<T> result)
    {
        var statusCode = result.Failure switch
        {
            UserRealNameFailure.InvalidRequest => StatusCodes.Status400BadRequest,
            UserRealNameFailure.NotFound => StatusCodes.Status404NotFound,
            UserRealNameFailure.Conflict => StatusCodes.Status409Conflict,
            UserRealNameFailure.Internal => StatusCodes.Status500InternalServerError,
            _ => StatusCodes.Status500InternalServerError,
        };

        return StatusCode(
            statusCode,
            ApiResponse<T>.Fail(result.ErrorCode!, result.Message!));
    }
}
