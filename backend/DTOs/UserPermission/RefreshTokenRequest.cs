using System.ComponentModel.DataAnnotations;

namespace ShowtimeBackend.DTOs.UserPermission;

public sealed class RefreshTokenRequest
{
    [Required]
    [StringLength(256, MinimumLength = 32)]
    public string RefreshToken { get; init; } = null!;
}
