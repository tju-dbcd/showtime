using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.WebUtilities;
using ShowtimeBackend.Common.TicketSecurity;

namespace ShowtimeBackend.Tests.OrderTicket;

public sealed class HmacTicketTokenServiceTests
{
    private const string SigningKeyBase64 =
        "ERERERERERERERERERERERERERERERERERERERERERE=";

    [Fact]
    public void Generate_CreatesTokenThatValidatesWithOriginalTicketAndTime()
    {
        var service = CreateService();
        var issuedAt = new DateTimeOffset(2026, 8, 23, 10, 0, 0, TimeSpan.Zero);

        var credential = service.Generate(issuedAt);

        Assert.StartsWith("TKT", credential.TicketNo);
        Assert.True(service.TryValidate(credential.QrCode, out var payload));
        Assert.NotNull(payload);
        Assert.Equal(credential.TicketNo, payload.TicketNo);
        Assert.Equal(issuedAt.ToUnixTimeSeconds(), payload.IssuedAtUnixSeconds);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void TryValidate_RejectsAnyTamperedSignedSegment(int segmentIndex)
    {
        var service = CreateService();
        var credential = service.Generate(
            new DateTimeOffset(2026, 8, 23, 10, 0, 0, TimeSpan.Zero));
        var segments = credential.QrCode.Split('.');
        segments[segmentIndex] = $"{segments[segmentIndex]}A";

        var isValid = service.TryValidate(string.Join('.', segments), out var payload);

        Assert.False(isValid);
        Assert.Null(payload);
    }

    [Fact]
    public void TryValidate_RejectsTokenSignedWithAnotherKey()
    {
        var credential = CreateService().Generate(
            new DateTimeOffset(2026, 8, 23, 10, 0, 0, TimeSpan.Zero));
        var otherService = new HmacTicketTokenService(
            Options.Create(new TicketSecurityOptions
            {
                SigningKeyBase64 =
                    "IiIiIiIiIiIiIiIiIiIiIiIiIiIiIiIiIiIiIiIiIiI=",
            }));

        Assert.False(otherService.TryValidate(credential.QrCode, out _));
    }

    [Theory]
    [InlineData("")]
    [InlineData("v2.ticket.1.nonce.signature")]
    [InlineData("v1.too.few.parts")]
    [InlineData("v1.ticket.not-a-time.nonce.signature")]
    [InlineData("v1.ticket.1.nonce.not*base64")]
    public void TryValidate_RejectsMalformedOrUnknownVersionToken(string qrCode)
    {
        Assert.False(CreateService().TryValidate(qrCode, out var payload));
        Assert.Null(payload);
    }

    [Fact]
    public void Generate_UsesBoundedDatabaseSafeIdentifiersAndSixteenByteNonce()
    {
        var credential = CreateService().Generate(
            new DateTimeOffset(2026, 8, 23, 10, 0, 0, TimeSpan.Zero));
        var nonce = credential.QrCode.Split('.')[3];

        Assert.Equal(35, credential.TicketNo.Length);
        Assert.Equal(32, credential.AntiFakeCode.Length);
        Assert.Equal(16, WebEncoders.Base64UrlDecode(nonce).Length);
        Assert.InRange(credential.QrCode.Length, 1, 255);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-base64")]
    [InlineData("ERERERERERERERERERERERERERERERERERERERER")]
    public void OptionsValidator_RejectsInvalidOrShortDecodedKey(string key)
    {
        var result = new TicketSecurityOptionsValidator().Validate(
            Options.DefaultName,
            new TicketSecurityOptions { SigningKeyBase64 = key });

        Assert.True(result.Failed);
    }

    [Fact]
    public void OptionsValidator_AcceptsBase64EncodingOfThirtyTwoBytes()
    {
        var result = new TicketSecurityOptionsValidator().Validate(
            Options.DefaultName,
            new TicketSecurityOptions { SigningKeyBase64 = SigningKeyBase64 });

        Assert.True(result.Succeeded);
    }

    private static HmacTicketTokenService CreateService() => new(
        Options.Create(new TicketSecurityOptions
        {
            SigningKeyBase64 = SigningKeyBase64,
        }));
}
