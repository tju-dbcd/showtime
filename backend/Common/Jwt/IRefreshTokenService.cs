namespace ShowtimeBackend.Common.Jwt;

public interface IRefreshTokenService
{
    IssuedRefreshToken Issue(long sessionId, DateTime expiresAtUtc);

    bool TryParseAndVerify(
        string rawToken,
        out ParsedRefreshToken? parsedToken);

    bool FixedTimeEquals(string storedHash, string presentedHash);
}

public sealed record IssuedRefreshToken(
    long SessionId,
    string RawToken,
    string TokenHash,
    DateTime ExpiresAtUtc);

public sealed record ParsedRefreshToken(
    long SessionId,
    string RawToken,
    string TokenHash);
