using Microsoft.Extensions.Options;
using ShowtimeBackend.Common.Jwt;

namespace ShowtimeBackend.Tests.UserPermission;

public sealed class RefreshTokenServiceTests
{
    private static readonly DateTime ExpiresAt =
        new(2030, 1, 8, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Issue_ProducesUniqueAuthenticatedTokensAndStableHashes()
    {
        var service = CreateService();

        var first = service.Issue(42, ExpiresAt);
        var second = service.Issue(42, ExpiresAt);

        Assert.NotEqual(first.RawToken, second.RawToken);
        Assert.Equal(64, first.TokenHash.Length);
        Assert.Equal(ExpiresAt, first.ExpiresAtUtc);
        Assert.True(service.TryParseAndVerify(first.RawToken, out var parsed));
        Assert.Equal(42, parsed!.SessionId);
        Assert.True(service.FixedTimeEquals(first.TokenHash, parsed.TokenHash));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void TryParseAndVerify_RejectsEveryTamperedSegment(int segment)
    {
        var service = CreateService();
        var issued = service.Issue(42, ExpiresAt);
        var parts = issued.RawToken.Split('.');
        parts[segment] = segment switch
        {
            0 => "v2",
            1 => "43",
            _ => parts[segment][..^1] + (parts[segment][^1] == 'A' ? "B" : "A"),
        };

        Assert.False(service.TryParseAndVerify(
            string.Join('.', parts),
            out _));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-token")]
    [InlineData("v1.01.value.value")]
    public void TryParseAndVerify_RejectsMalformedTokens(string token)
    {
        Assert.False(CreateService().TryParseAndVerify(token, out _));
    }

    [Fact]
    public void FixedTimeEquals_RejectsMalformedOrDifferentHashes()
    {
        var service = CreateService();
        var first = service.Issue(1, ExpiresAt);
        var second = service.Issue(1, ExpiresAt);

        Assert.False(service.FixedTimeEquals(first.TokenHash, second.TokenHash));
        Assert.False(service.FixedTimeEquals(first.TokenHash, "not-hex"));
    }

    private static RefreshTokenService CreateService() =>
        new(Options.Create(new JwtOptions
        {
            Key = AuthTestFactory.TestKey,
            Issuer = AuthTestFactory.TestIssuer,
            Audience = AuthTestFactory.TestAudience,
            ExpirationMinutes = 15,
            RefreshTokenExpirationDays = 7,
        }));
}
