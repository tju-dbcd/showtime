namespace ShowtimeBackend.Services.Auth;

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
}
