using Xunit;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using AkademVault_API.Data;
using AkademVault_API.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;

namespace Tests;

public class AuthMeTests
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

    [Fact]
    public async Task Me_ShouldReturnUnauthorized_WhenUserNotInDb()
    {

        var context = GetDbContext();
        var controller = new AuthController(context);
        var userId = Guid.NewGuid();

        SetUser(controller, userId);


        var result = await controller.Me();


        result.Should().BeOfType<UnauthorizedResult>();
    }

    [Fact]
    public async Task Me_ShouldReturnUserWithIsOwnerFalse_WhenNotOwner()
    {

        var context = GetDbContext();
        var controller = new AuthController(context);
        var userId = Guid.NewGuid();
        var groupId = Guid.NewGuid();

        context.Users.Add(new User { Id = userId, Username = "student", Email = "s@uni.ua", GroupId = groupId });
        context.Groups.Add(new Group { Id = groupId, Name = "КН-31", OwnerId = Guid.NewGuid() });
        await context.SaveChangesAsync();

        SetUser(controller, userId);


        var result = await controller.Me();


        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var value = ok.Value!;
        value.GetType().GetProperty("isOwner")!.GetValue(value).Should().Be(false);
        value.GetType().GetProperty("groupId")!.GetValue(value).Should().Be(groupId);
        value.GetType().GetProperty("username")!.GetValue(value).Should().Be("student");
    }

    [Fact]
    public async Task Me_ShouldReturnIsOwnerTrue_WhenOwner()
    {

        var context = GetDbContext();
        var controller = new AuthController(context);
        var userId = Guid.NewGuid();
        var groupId = Guid.NewGuid();

        context.Users.Add(new User { Id = userId, Username = "starosta", Email = "s@uni.ua", GroupId = groupId });
        context.Groups.Add(new Group { Id = groupId, Name = "КН-31", OwnerId = userId });
        await context.SaveChangesAsync();

        SetUser(controller, userId);


        var result = await controller.Me();


        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value!.GetType().GetProperty("isOwner")!.GetValue(ok.Value).Should().Be(true);
    }
}
