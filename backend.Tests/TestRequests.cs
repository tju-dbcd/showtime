using ShowtimeBackend.DTOs.UserPermission;

namespace ShowtimeBackend.Tests;

internal static class TestRequests
{
    public const string Password = "Secure123";

    public static RegisterRequest ValidRegistration() => new()
    {
        UserName = "alice",
        Password = Password,
        Phone = "+8613800138000",
        Nickname = "Alice",
        Email = "alice@example.com",
    };

    public static LoginRequest Login(string account, string password = Password) =>
        new()
        {
            Account = account,
            Password = password,
        };
}
