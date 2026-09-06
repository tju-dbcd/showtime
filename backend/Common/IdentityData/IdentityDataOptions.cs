namespace ShowtimeBackend.Common.IdentityData;

public sealed class IdentityDataOptions
{
    public const string SectionName = "IdentityData";

    public string EncryptionKey { get; set; } = string.Empty;
}
