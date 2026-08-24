using Microsoft.Extensions.Options;

namespace ShowtimeBackend.Common.TicketSecurity;

public sealed class TicketSecurityOptionsValidator
    : IValidateOptions<TicketSecurityOptions>
{
    private const int MinimumKeyBytes = 32;

    public ValidateOptionsResult Validate(
        string? name,
        TicketSecurityOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.SigningKeyBase64))
        {
            return ValidateOptionsResult.Fail(
                "TicketSecurity:SigningKeyBase64 is required.");
        }

        byte[] decodedKey;
        try
        {
            decodedKey = Convert.FromBase64String(options.SigningKeyBase64);
        }
        catch (FormatException)
        {
            return ValidateOptionsResult.Fail(
                "TicketSecurity:SigningKeyBase64 must be valid Base64.");
        }

        return decodedKey.Length >= MinimumKeyBytes
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                "TicketSecurity:SigningKeyBase64 must decode to at least 32 bytes.");
    }
}
