using System.ComponentModel.DataAnnotations;

namespace ShowtimeBackend.DTOs.Auth;

public sealed record RegisterRequest
{
    [Required]
    [StringLength(50, MinimumLength = 3)]
    [RegularExpression(
        @"^[A-Za-z][A-Za-z0-9_]{2,49}$",
        ErrorMessage = "UserName must start with a letter and contain only letters, numbers, and underscores.")]
    public string UserName { get; init; } = null!;

    [Required]
    [StringLength(128, MinimumLength = 8)]
    [RegularExpression(
        @"^(?=.*[A-Za-z])(?=.*[0-9])[^\r\n]{8,128}$",
        ErrorMessage = "Password must contain at least one letter and one number.")]
    public string Password { get; init; } = null!;

    [Required]
    [StringLength(20)]
    [RegularExpression(
        @"^(?:\+?[0-9]{6,19}|[0-9]{20})$",
        ErrorMessage = "Phone must contain 6 to 20 digits and may start with +.")]
    public string Phone { get; init; } = null!;

    [StringLength(50)]
    public string? Nickname { get; init; }

    [StringLength(100)]
    [EmailAddress]
    public string? Email { get; init; }
}
