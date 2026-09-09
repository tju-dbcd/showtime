using Microsoft.Extensions.Options;

namespace ShowtimeBackend.Common.IdentityData;

public sealed class IdentityDataOptionsValidator : IValidateOptions<IdentityDataOptions>
{
    private const int RequiredKeySizeInBytes = 32;

    public ValidateOptionsResult Validate(string? name, IdentityDataOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.EncryptionKey))
        {
            return ValidateOptionsResult.Fail(
                "IdentityData:EncryptionKey is required.");
        }

        byte[] key;
        try
        {
            key = Convert.FromBase64String(options.EncryptionKey);
        }
        catch (FormatException)
        {
            return ValidateOptionsResult.Fail(
                "IdentityData:EncryptionKey must be a valid Base64 value.");
        }

        try
        {
            return key.Length == RequiredKeySizeInBytes
                ? ValidateOptionsResult.Success
                : ValidateOptionsResult.Fail(
                    $"IdentityData:EncryptionKey must decode to exactly {RequiredKeySizeInBytes} bytes.");
        }
        finally
        {
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(key);
        }
    }
}
