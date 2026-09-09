using System.ComponentModel.DataAnnotations;

namespace ShowtimeBackend.DTOs.UserPermission;

public sealed record CreateUserRealNameRequest
{
    [Required]
    [StringLength(50, MinimumLength = 2)]
    public string RealName { get; init; } = null!;

    [Required]
    [StringLength(32, MinimumLength = 18)]
    public string IdCardNo { get; init; } = null!;

    public bool IsDefault { get; init; }
}
