using System.ComponentModel.DataAnnotations;

namespace ShowtimeBackend.DTOs.UserPermission;

public sealed record LoginRequest
{
    [Required]
    [StringLength(100)]
    public string Account { get; init; } = null!;

    [Required]
    [StringLength(128)]
    public string Password { get; init; } = null!;
}
