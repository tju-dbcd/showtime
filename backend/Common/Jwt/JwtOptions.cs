using System.ComponentModel.DataAnnotations;

namespace ShowtimeBackend.Common.Jwt;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    [Required]
    public string Key { get; init; } = null!;

    [Required]
    public string Issuer { get; init; } = null!;

    [Required]
    public string Audience { get; init; } = null!;

    [Range(1, int.MaxValue)]
    public int ExpirationMinutes { get; init; } = 15;

    [Range(1, int.MaxValue)]
    public int RefreshTokenExpirationDays { get; init; } = 7;
}
