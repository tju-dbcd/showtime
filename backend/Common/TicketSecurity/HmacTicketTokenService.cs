using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace ShowtimeBackend.Common.TicketSecurity;

public sealed class HmacTicketTokenService : ITicketTokenService
{
    private const string Version = "v1";
    private readonly byte[] _signingKey;

    public HmacTicketTokenService(IOptions<TicketSecurityOptions> options)
    {
        _signingKey = Convert.FromBase64String(options.Value.SigningKeyBase64);
    }

    public TicketCredential Generate(DateTimeOffset issuedAt)
    {
        var ticketNo = $"TKT{Guid.NewGuid():N}".ToUpperInvariant();
        var antiFakeCode = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        var nonce = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(16));
        var issuedAtUnixSeconds = issuedAt.ToUnixTimeSeconds();
        var signature = CreateSignature(ticketNo, issuedAtUnixSeconds, nonce);
        var qrCode = $"{Version}.{ticketNo}.{issuedAtUnixSeconds}.{nonce}.{signature}";

        return new TicketCredential(ticketNo, antiFakeCode, qrCode);
    }

    public bool TryValidate(string qrCode, out TicketTokenPayload? payload)
    {
        payload = null;
        var parts = qrCode.Split('.');
        if (parts.Length != 5 || parts[0] != Version ||
            string.IsNullOrWhiteSpace(parts[1]) ||
            !long.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var issuedAt) ||
            string.IsNullOrWhiteSpace(parts[3]))
        {
            return false;
        }

        byte[] actualSignature;
        try
        {
            actualSignature = WebEncoders.Base64UrlDecode(parts[4]);
        }
        catch (FormatException)
        {
            return false;
        }

        var expectedSignature = WebEncoders.Base64UrlDecode(
            CreateSignature(parts[1], issuedAt, parts[3]));
        if (!CryptographicOperations.FixedTimeEquals(expectedSignature, actualSignature))
        {
            return false;
        }

        payload = new TicketTokenPayload(parts[1], issuedAt, parts[3]);
        return true;
    }

    private string CreateSignature(
        string ticketNo,
        long issuedAtUnixSeconds,
        string nonce)
    {
        var canonical = string.Join(
            '\n',
            Version,
            ticketNo,
            issuedAtUnixSeconds.ToString(CultureInfo.InvariantCulture),
            nonce);
        var signature = HMACSHA256.HashData(
            _signingKey,
            Encoding.UTF8.GetBytes(canonical));
        return WebEncoders.Base64UrlEncode(signature);
    }
}
