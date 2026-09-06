using ShowtimeBackend.DTOs.UserPermission;

namespace ShowtimeBackend.Services.UserPermission;

public interface IUserRealNameService
{
    Task<UserRealNameResult<IReadOnlyList<UserRealNameResponse>>> ListAsync(
        long userId,
        CancellationToken cancellationToken);

    Task<UserRealNameResult<UserRealNameResponse>> CreateAsync(
        long userId,
        string actor,
        CreateUserRealNameRequest request,
        CancellationToken cancellationToken);

    Task<UserRealNameResult<UserRealNameResponse>> UpdateAsync(
        long userId,
        string actor,
        long realNameId,
        UpdateUserRealNameRequest request,
        CancellationToken cancellationToken);

    Task<UserRealNameResult<UserRealNameResponse>> SetDefaultAsync(
        long userId,
        string actor,
        long realNameId,
        CancellationToken cancellationToken);

    Task<UserRealNameResult<bool>> DeleteAsync(
        long userId,
        string actor,
        long realNameId,
        CancellationToken cancellationToken);
}
