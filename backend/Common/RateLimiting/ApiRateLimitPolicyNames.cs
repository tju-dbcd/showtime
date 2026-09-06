namespace ShowtimeBackend.Common.RateLimiting;

public static class ApiRateLimitPolicyNames
{
    public const string Login = "auth-login";
    public const string Register = "auth-register";
    public const string Refresh = "auth-refresh";

    public static bool IsDedicated(string? policyName) =>
        policyName is Login or Register or Refresh;
}
