namespace ShowtimeBackend.Common.IdentityData;

public sealed record UnprotectedIdentityData(string Value, bool IsLegacy);

public interface IIdentityDataProtector
{
    string Protect(string plaintext);

    UnprotectedIdentityData Unprotect(string storedValue);

    string MaskStoredValue(string storedValue);

    bool IsProtected(string value);
}
