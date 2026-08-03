using System.ComponentModel.DataAnnotations;

namespace ShowtimeBackend.DTOs.Auth;

public sealed record LoginRequest
{
    [Required]
    [StringLength(100)]
    public string Account { get; init; } = null!;

    [Required]
    [StringLength(128)]
    public string Password { get; init; } = null!;
}
