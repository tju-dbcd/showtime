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

    /// <summary>
    /// 轮换后的旧 refresh token 在多少秒内重放视为合法重试（并发/网络重试不锁会话）；
    /// 0 表示不宽限，旧 token 重放一律拒绝。
    /// </summary>
    [Range(0, 3600)]
    public int RefreshTokenReuseGraceSeconds { get; init; } = 30;
}
