namespace ShowtimeBackend.Common.IdentityData;

public sealed class IdentityDataProtectionException : Exception
{
    public IdentityDataProtectionException(string message)
        : base(message)
    {
    }

    public IdentityDataProtectionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
