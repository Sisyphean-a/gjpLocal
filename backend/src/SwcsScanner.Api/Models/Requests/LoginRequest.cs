using System.ComponentModel.DataAnnotations;

namespace SwcsScanner.Api.Models.Requests;

public sealed class LoginRequest
{
    [Required]
    [MaxLength(64)]
    public string Username { get; init; } = string.Empty;

    [Required]
    [MaxLength(128)]
    public string Password { get; init; } = string.Empty;
}
