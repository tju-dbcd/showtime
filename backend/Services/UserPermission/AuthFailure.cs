namespace ShowtimeBackend.Services.UserPermission;

public enum AuthFailure
{
    None = 0,
    UserNameTaken,
    PhoneTaken,
    EmailTaken,
    InvalidCredentials,
    AccountDisabled,
    AccountLocked,
    DefaultRoleUnavailable,
    UserNotFound,
    InvalidAvatarUrl,
}
