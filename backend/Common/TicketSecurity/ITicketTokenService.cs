namespace ShowtimeBackend.Common.TicketSecurity;

public sealed record TicketCredential(
    string TicketNo,
    string AntiFakeCode,
    string QrCode);

public sealed record TicketTokenPayload(
    string TicketNo,
    long IssuedAtUnixSeconds,
    string Nonce);

public interface ITicketTokenService
{
    TicketCredential Generate(DateTimeOffset issuedAt);

    bool TryValidate(string qrCode, out TicketTokenPayload? payload);
}
