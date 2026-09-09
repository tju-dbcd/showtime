using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace ShowtimeBackend.Common.IdentityData;

public sealed class AesGcmIdentityDataProtector : IIdentityDataProtector, IDisposable
{
    private const string VersionPrefix = "v1.";
    private const int NonceSizeInBytes = 12;
    private const int TagSizeInBytes = 16;

    private readonly byte[] _key;
    private bool _disposed;

    public AesGcmIdentityDataProtector(IOptions<IdentityDataOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        try
        {
            _key = Convert.FromBase64String(options.Value.EncryptionKey);
        }
        catch (FormatException exception)
        {
            throw new IdentityDataProtectionException(
                "The identity-data encryption key is invalid.",
                exception);
        }

        if (_key.Length != 32)
        {
            CryptographicOperations.ZeroMemory(_key);
            throw new IdentityDataProtectionException(
                "The identity-data encryption key has an invalid length.");
        }
    }

    public string Protect(string plaintext)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(plaintext);

        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var nonce = new byte[NonceSizeInBytes];
        var tag = new byte[TagSizeInBytes];
        var ciphertext = new byte[plaintextBytes.Length];

        try
        {
            RandomNumberGenerator.Fill(nonce);
            using var aesGcm = new AesGcm(_key, TagSizeInBytes);
            aesGcm.Encrypt(nonce, plaintextBytes, ciphertext, tag);

            return string.Concat(
                VersionPrefix,
                Convert.ToBase64String(nonce),
                ".",
                Convert.ToBase64String(tag),
                ".",
                Convert.ToBase64String(ciphertext));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintextBytes);
            CryptographicOperations.ZeroMemory(ciphertext);
            CryptographicOperations.ZeroMemory(tag);
            CryptographicOperations.ZeroMemory(nonce);
        }
    }

    public UnprotectedIdentityData Unprotect(string storedValue)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(storedValue);

        if (!IsProtected(storedValue))
        {
            if (LooksLikeVersionedPayload(storedValue))
            {
                throw new IdentityDataProtectionException(
                    "The identity-data payload version is not supported.");
            }

            return new UnprotectedIdentityData(storedValue, IsLegacy: true);
        }

        var parts = storedValue.Split('.');
        if (parts.Length != 4 || parts[0] != "v1")
        {
            throw new IdentityDataProtectionException(
                "The identity-data payload format is invalid.");
        }

        byte[] nonce;
        byte[] tag;
        byte[] ciphertext;
        try
        {
            nonce = Convert.FromBase64String(parts[1]);
            tag = Convert.FromBase64String(parts[2]);
            ciphertext = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException exception)
        {
            throw new IdentityDataProtectionException(
                "The identity-data payload format is invalid.",
                exception);
        }

        var plaintext = new byte[ciphertext.Length];
        try
        {
            if (nonce.Length != NonceSizeInBytes || tag.Length != TagSizeInBytes)
            {
                throw new IdentityDataProtectionException(
                    "The identity-data payload parameters are invalid.");
            }

            using var aesGcm = new AesGcm(_key, TagSizeInBytes);
            aesGcm.Decrypt(nonce, ciphertext, tag, plaintext);
            return new UnprotectedIdentityData(
                Encoding.UTF8.GetString(plaintext),
                IsLegacy: false);
        }
        catch (CryptographicException exception)
        {
            throw new IdentityDataProtectionException(
                "The identity-data payload failed authentication.",
                exception);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
            CryptographicOperations.ZeroMemory(ciphertext);
            CryptographicOperations.ZeroMemory(tag);
            CryptographicOperations.ZeroMemory(nonce);
        }
    }

    public string MaskStoredValue(string storedValue)
    {
        var plaintext = Unprotect(storedValue).Value;
        if (plaintext.Length <= 7)
        {
            return new string('*', plaintext.Length);
        }

        return string.Concat(
            plaintext.AsSpan(0, 3),
            new string('*', plaintext.Length - 7),
            plaintext.AsSpan(plaintext.Length - 4));
    }

    public bool IsProtected(string value) =>
        value.StartsWith(VersionPrefix, StringComparison.Ordinal);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        CryptographicOperations.ZeroMemory(_key);
        _disposed = true;
    }

    private static bool LooksLikeVersionedPayload(string value) =>
        value.Length >= 3 &&
        value[0] == 'v' &&
        char.IsAsciiDigit(value[1]) &&
        value[2] == '.';
}
