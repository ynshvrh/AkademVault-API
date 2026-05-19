using Xunit;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using AkademVault_API.Data;
using AkademVault_API.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;

namespace Tests;

// Tests for PUT /auth/profile and POST /auth/change-password — covers username uniqueness and BCrypt verification.
public class AuthProfileTests
{
    private AppDbContext GetDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    private static void SetUser(AuthController controller, Guid userId)
    {
        var claims = new List<Claim> { new Claim(ClaimTypes.NameIdentifier, userId.ToString()) };
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims)) }
        };
    }


    // Username update succeeds and persists when the new name is not taken by anyone else.
    [Fact]
    public async Task UpdateProfile_ChangesUsername_WhenAvailable()
    {
        var ctx = GetDbContext();
        var ctrl = new AuthController(ctx);
        var userId = Guid.NewGuid();
        ctx.Users.Add(new User { Id = userId, Username = "oldname", Email = "u@x.ua", PasswordHash = "h" });
        await ctx.SaveChangesAsync();
        SetUser(ctrl, userId);

        var result = await ctrl.UpdateProfile(new UpdateProfileRequest { Username = "newname" });

        result.Should().BeOfType<OkObjectResult>();
        (await ctx.Users.FindAsync(userId))!.Username.Should().Be("newname");
    }

    // Username update is rejected with 400 when another user already owns that name.
    [Fact]
    public async Task UpdateProfile_Rejects_WhenUsernameTaken()
    {
        var ctx = GetDbContext();
        var ctrl = new AuthController(ctx);
        var userId = Guid.NewGuid();
        ctx.Users.Add(new User { Id = userId, Username = "me", Email = "m@x.ua", PasswordHash = "h" });
        ctx.Users.Add(new User { Id = Guid.NewGuid(), Username = "taken", Email = "t@x.ua", PasswordHash = "h" });
        await ctx.SaveChangesAsync();
        SetUser(ctrl, userId);

        var result = await ctrl.UpdateProfile(new UpdateProfileRequest { Username = "taken" });

        result.Should().BeOfType<BadRequestObjectResult>();
        (await ctx.Users.FindAsync(userId))!.Username.Should().Be("me");
    }

    // Password change works end-to-end: new hash verifies, old hash no longer matches.
    [Fact]
    public async Task ChangePassword_Succeeds_WithCorrectCurrent()
    {
        var ctx = GetDbContext();
        var ctrl = new AuthController(ctx);
        var userId = Guid.NewGuid();
        var oldHash = BCrypt.Net.BCrypt.HashPassword("OldP@ss1");
        ctx.Users.Add(new User { Id = userId, Username = "u", Email = "u@x.ua", PasswordHash = oldHash });
        await ctx.SaveChangesAsync();
        SetUser(ctrl, userId);

        var result = await ctrl.ChangePassword(new ChangePasswordRequest
        {
            CurrentPassword = "OldP@ss1",
            NewPassword = "NewP@ss1"
        });

        result.Should().BeOfType<OkObjectResult>();
        var updated = await ctx.Users.FindAsync(userId);
        BCrypt.Net.BCrypt.Verify("NewP@ss1", updated!.PasswordHash).Should().BeTrue();
        BCrypt.Net.BCrypt.Verify("OldP@ss1", updated.PasswordHash).Should().BeFalse();
    }

    // Wrong current password yields 400 and leaves the stored hash untouched.
    [Fact]
    public async Task ChangePassword_Rejects_WithWrongCurrent()
    {
        var ctx = GetDbContext();
        var ctrl = new AuthController(ctx);
        var userId = Guid.NewGuid();
        var oldHash = BCrypt.Net.BCrypt.HashPassword("OldP@ss1");
        ctx.Users.Add(new User { Id = userId, Username = "u", Email = "u@x.ua", PasswordHash = oldHash });
        await ctx.SaveChangesAsync();
        SetUser(ctrl, userId);

        var result = await ctrl.ChangePassword(new ChangePasswordRequest
        {
            CurrentPassword = "WrongP@ss",
            NewPassword = "NewP@ss1"
        });

        result.Should().BeOfType<BadRequestObjectResult>();
        var updated = await ctx.Users.FindAsync(userId);
        BCrypt.Net.BCrypt.Verify("OldP@ss1", updated!.PasswordHash).Should().BeTrue();
    }
}
