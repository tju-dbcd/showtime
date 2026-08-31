namespace ShowtimeBackend.Common.Jwt;

public sealed record JwtTokenResult(
    string AccessToken,
    DateTime ExpiresAtUtc);
