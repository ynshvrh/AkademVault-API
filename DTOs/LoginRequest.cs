using System.ComponentModel.DataAnnotations;

namespace AkademVault_API.DTOs;

// Request body for POST /auth/login.
public class LoginRequest
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required]
    public string Password { get; set; } = string.Empty;
}
