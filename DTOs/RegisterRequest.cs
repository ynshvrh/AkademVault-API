using System.ComponentModel.DataAnnotations;

namespace AkademVault_API.DTOs;

public class RegisterRequest
{
    [Required(ErrorMessage = "Email є обов'язковим")]
    [EmailAddress(ErrorMessage = "Некоректний формат Email")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Юзернейм є обов'язковим")]
    [StringLength(20, MinimumLength = 3, ErrorMessage = "Юзернейм має бути від 3 до 20 символів")]
    public string Username { get; set; } = string.Empty;

    [Required(ErrorMessage = "Пароль є обов'язковим")]
    [MinLength(6, ErrorMessage = "Пароль має бути не менше 6 символів")]
    public string Password { get; set; } = string.Empty;
}