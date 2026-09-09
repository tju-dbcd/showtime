using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace ShowtimeBackend.Common.Jwt;

public sealed class RefreshTokenService : IRefreshTokenService
{
    private const string Version = "v1";
    private const int NonceLength = 32;
    private const int MacLength = 32;
    private const int HashHexLength = 64;
    private const int MaxRawTokenLength = 256;
    private static readonly byte[] KeyDerivationContext =
        Encoding.UTF8.GetBytes("Showtime.RefreshToken.v1");

    private readonly byte[] _signingKey;

    public RefreshTokenService(IOptions<JwtOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var jwtKey = Encoding.UTF8.GetBytes(options.Value.Key);
        _signingKey = HMACSHA256.HashData(jwtKey, KeyDerivationContext);
    }

    public IssuedRefreshToken Issue(long sessionId, DateTime expiresAtUtc)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sessionId);

        var nonce = WebEncoders.Base64UrlEncode(
            RandomNumberGenerator.GetBytes(NonceLength));
        var payload = string.Create(
            CultureInfo.InvariantCulture,
            $"{Version}.{sessionId}.{nonce}");
        var mac = HMACSHA256.HashData(
            _signingKey,
            Encoding.UTF8.GetBytes(payload));
        var rawToken = $"{payload}.{WebEncoders.Base64UrlEncode(mac)}";

        return new IssuedRefreshToken(
            sessionId,
            rawToken,
            ComputeHash(rawToken),
            DateTime.SpecifyKind(expiresAtUtc, DateTimeKind.Utc));
    }

    public bool TryParseAndVerify(
        string rawToken,
        out ParsedRefreshToken? parsedToken)
    {
        parsedToken = null;
        if (string.IsNullOrWhiteSpace(rawToken)
            || rawToken.Length > MaxRawTokenLength)
        {
            return false;
        }

        var parts = rawToken.Split('.');
        if (parts.Length != 4
            || parts[0] != Version
            || !long.TryParse(
                parts[1],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var sessionId)
            || sessionId <= 0
            || parts[1] != sessionId.ToString(CultureInfo.InvariantCulture))
        {
            return false;
        }

        byte[] nonce;
        byte[] presentedMac;
        try
        {
            nonce = WebEncoders.Base64UrlDecode(parts[2]);
            presentedMac = WebEncoders.Base64UrlDecode(parts[3]);
        }
        catch (FormatException)
        {
            return false;
        }

        if (nonce.Length != NonceLength || presentedMac.Length != MacLength)
        {
            return false;
        }

        var payload = $"{parts[0]}.{parts[1]}.{parts[2]}";
        var expectedMac = HMACSHA256.HashData(
            _signingKey,
            Encoding.UTF8.GetBytes(payload));
        if (!CryptographicOperations.FixedTimeEquals(expectedMac, presentedMac))
        {
            return false;
        }

        parsedToken = new ParsedRefreshToken(
            sessionId,
            rawToken,
            ComputeHash(rawToken));
        return true;
    }

    public bool FixedTimeEquals(string storedHash, string presentedHash)
    {
        if (storedHash.Length != HashHexLength
            || presentedHash.Length != HashHexLength)
        {
            return false;
        }

        try
        {
            return CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(storedHash),
                Convert.FromHexString(presentedHash));
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string ComputeHash(string rawToken) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));
}
