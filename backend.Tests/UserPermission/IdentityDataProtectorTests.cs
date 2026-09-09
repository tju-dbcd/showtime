using Microsoft.Extensions.Options;
using ShowtimeBackend.Common.IdentityData;

namespace ShowtimeBackend.Tests.UserPermission;

public sealed class IdentityDataProtectorTests
{
    private const string TestKey =
        "QkJCQkJCQkJCQkJCQkJCQkJCQkJCQkJCQkJCQkJCQkI=";
    private const string IdCardNo = "31010119900101123X";

    [Fact]
    public void Protect_RoundTripsAndUsesRandomNonce()
    {
        using var protector = CreateProtector();

        var first = protector.Protect(IdCardNo);
        var second = protector.Protect(IdCardNo);

        Assert.StartsWith("v1.", first);
        Assert.NotEqual(first, second);
        Assert.DoesNotContain(IdCardNo, first, StringComparison.Ordinal);
        Assert.True(first.Length < 255);
        Assert.Equal(IdCardNo, protector.Unprotect(first).Value);
        Assert.Equal(IdCardNo, protector.Unprotect(second).Value);
    }

    [Fact]
    public void Unprotect_TamperedTagFailsAuthentication()
    {
        using var protector = CreateProtector();
        var parts = protector.Protect(IdCardNo).Split('.');
        var tag = Convert.FromBase64String(parts[2]);
        tag[0] ^= 0x01;
        parts[2] = Convert.ToBase64String(tag);

        var exception = Assert.Throws<IdentityDataProtectionException>(
            () => protector.Unprotect(string.Join('.', parts)));

        Assert.Contains("authentication", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Unprotect_LegacyPlaintextIsIdentified_AndUnknownVersionIsRejected()
    {
        using var protector = CreateProtector();

        var legacy = protector.Unprotect(IdCardNo);

        Assert.True(legacy.IsLegacy);
        Assert.Equal(IdCardNo, legacy.Value);
        Assert.Throws<IdentityDataProtectionException>(
            () => protector.Unprotect("v2.invalid.payload.value"));
    }

    [Fact]
    public void MaskStoredValue_ReturnsOnlyFirstThreeAndLastFourCharacters()
    {
        using var protector = CreateProtector();

        var masked = protector.MaskStoredValue(protector.Protect(IdCardNo));

        Assert.Equal("310***********123X", masked);
        Assert.DoesNotContain("19900101", masked, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-base64")]
    [InlineData("AQID")]
    public void OptionsValidator_RejectsInvalidKeys(string key)
    {
        var result = new IdentityDataOptionsValidator().Validate(
            null,
            new IdentityDataOptions { EncryptionKey = key });

        Assert.True(result.Failed);
        if (key.Length > 0)
        {
            Assert.DoesNotContain(
                key,
                result.FailureMessage ?? string.Empty,
                StringComparison.Ordinal);
        }
    }

    [Fact]
    public void OptionsValidator_AcceptsThirtyTwoByteKey()
    {
        var result = new IdentityDataOptionsValidator().Validate(
            null,
            new IdentityDataOptions { EncryptionKey = TestKey });

        Assert.True(result.Succeeded);
    }

    internal static AesGcmIdentityDataProtector CreateProtector() => new(
        Options.Create(new IdentityDataOptions { EncryptionKey = TestKey }));
}
