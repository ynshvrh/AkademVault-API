using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using AkademVault_API.Data;
using AkademVault_API.DTOs;
using AkademVault_API.Models;

// Auth endpoints: registration, cookie-based login/logout, profile and password updates.
[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly IAntiforgery _antiforgery;

    public AuthController(AppDbContext context, IAntiforgery antiforgery)
    {
        _context = context;
        _antiforgery = antiforgery;
    }

    // Rotates the readable XSRF-TOKEN cookie so the SPA can echo it back as the X-XSRF-TOKEN header.
    private void IssueAntiforgeryCookies(ClaimsPrincipal principal)
    {
        HttpContext.User = principal;
        var tokens = _antiforgery.GetAndStoreTokens(HttpContext);
        Response.Cookies.Append("XSRF-TOKEN", tokens.RequestToken!, new CookieOptions
        {
            HttpOnly = false,
            SameSite = SameSiteMode.Lax,
            Secure = Request.IsHttps
        });
    }

    // Creates a new user with a BCrypt-hashed password; rejects duplicate emails.
    [IgnoreAntiforgeryToken]
    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        if (await _context.Users.AnyAsync(u => u.Email == request.Email))
        {
            return BadRequest(new { message = "Користувач з таким Email вже існує" });
        }


        string passwordHash = BCrypt.Net.BCrypt.HashPassword(request.Password);

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = request.Email,
            Username = request.Username,
            PasswordHash = passwordHash,
            CreatedAt = DateTime.UtcNow
        };

        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        return Ok(new { message = "Реєстрація успішна" });
    }

    // Verifies credentials with BCrypt and signs the user in via auth cookie; also issues a fresh CSRF token.
    [IgnoreAntiforgeryToken]
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == request.Email);


        if (user == null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
        {
            return Unauthorized(new { message = "Невірний email або пароль" });
        }

        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.Username),
            new Claim(ClaimTypes.Email, user.Email)
        };

        var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);

        var principal = new ClaimsPrincipal(claimsIdentity);
        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal);

        IssueAntiforgeryCookies(principal);

        return Ok(new { username = user.Username, email = user.Email });
    }

    // Clears the auth cookie and rotates CSRF tokens to an anonymous principal.
    [IgnoreAntiforgeryToken]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        IssueAntiforgeryCookies(new ClaimsPrincipal(new ClaimsIdentity()));
        return Ok(new { message = "Вихід успішний" });
    }


    // Returns the current session user plus a derived isOwner flag for the SPA to gate Owner-only UI.
    [Authorize]
    [HttpGet("me")]
    public async Task<IActionResult> Me()
    {
        var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        var user = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);

        if (user == null)
            return Unauthorized();

        var isOwner = user.GroupId.HasValue
            && await _context.Groups.AsNoTracking().AnyAsync(g => g.Id == user.GroupId && g.OwnerId == userId);

        return Ok(new
        {
            id = user.Id,
            username = user.Username,
            email = user.Email,
            groupId = user.GroupId,
            isOwner
        });
    }


    // Updates the username after enforcing global uniqueness (case-sensitive trim).
    [Authorize]
    [HttpPut("profile")]
    public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileRequest request)
    {
        var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        var user = await _context.Users.FindAsync(userId);
        if (user == null) return Unauthorized();

        var trimmed = request.Username.Trim();
        if (await _context.Users.AnyAsync(u => u.Username == trimmed && u.Id != userId))
            return BadRequest(new { message = "Цей юзернейм уже зайнято" });

        user.Username = trimmed;
        await _context.SaveChangesAsync();
        return Ok(new { username = user.Username });
    }


    // Replaces the password hash after verifying the current one; session cookie stays valid.
    [Authorize]
    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request)
    {
        var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        var user = await _context.Users.FindAsync(userId);
        if (user == null) return Unauthorized();

        if (!BCrypt.Net.BCrypt.Verify(request.CurrentPassword, user.PasswordHash))
            return BadRequest(new { message = "Поточний пароль невірний" });

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
        await _context.SaveChangesAsync();
        return Ok(new { message = "Пароль змінено" });
    }
}

// Request body for PUT /auth/profile.
public class UpdateProfileRequest
{
    [Required(ErrorMessage = "Юзернейм обов'язковий")]
    [StringLength(20, MinimumLength = 3, ErrorMessage = "Юзернейм має бути від 3 до 20 символів")]
    public string Username { get; set; } = string.Empty;
}

// Request body for POST /auth/change-password.
public class ChangePasswordRequest
{
    [Required(ErrorMessage = "Поточний пароль обов'язковий")]
    public string CurrentPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "Новий пароль обов'язковий")]
    [MinLength(6, ErrorMessage = "Пароль має бути не менше 6 символів")]
    public string NewPassword { get; set; } = string.Empty;
}
