using Xunit;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using AkademVault_API.Controllers;
using AkademVault_API.Data;
using AkademVault_API.Models;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace Tests;

public class GroupControllerTests
{

    private AppDbContext GetDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    private static void SetUser(GroupController controller, Guid userId)
    {
        var claims = new List<Claim> { new Claim(ClaimTypes.NameIdentifier, userId.ToString()) };
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims)) }
        };
    }

    [Fact]
    public async Task CreateGroup_ShouldSuccess_AndCalculateExpiryDateCorrectlty()
    {

        var context = GetDbContext();
        var controller = new GroupController(context);


        var userId = Guid.NewGuid();
        var user = new User {
            Id = userId,
            Username = "yanosh_dev",
            Email = "test@student.ua",
            PasswordHash = "fake_hash"
        };
        context.Users.Add(user);
        await context.SaveChangesAsync();


        var claims = new List<Claim> { new Claim(ClaimTypes.NameIdentifier, userId.ToString()) };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        var principal = new ClaimsPrincipal(identity);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = principal }
        };

        var request = new CreateGroupRequest { Name = "КН-31", YearsOfStudy = 4 };


        var result = await controller.CreateGroup(request);


        var okResult = result as OkObjectResult;
        okResult.Should().NotBeNull();


        var groupInDb = await context.Groups.FirstOrDefaultAsync(g => g.Name == "КН-31");
        groupInDb.Should().NotBeNull();
        groupInDb!.OwnerId.Should().Be(userId);


        var expectedYear = DateTime.UtcNow.AddYears(4).Year;
        groupInDb.ExpiryDate.Year.Should().Be(expectedYear);


        var updatedUser = await context.Users.FindAsync(userId);
        updatedUser!.GroupId.Should().Be(groupInDb.Id);
    }

    [Fact]
    public async Task GetAll_ShouldReturnAllGroupsWithOwnerAndMemberCount()
    {

        var context = GetDbContext();
        var controller = new GroupController(context);
        var ownerId = Guid.NewGuid();
        var groupId = Guid.NewGuid();

        var owner = new User { Id = ownerId, Username = "starosta", GroupId = groupId };
        var member = new User { Id = Guid.NewGuid(), Username = "student", GroupId = groupId };
        context.Users.AddRange(owner, member);
        context.Groups.Add(new Group { Id = groupId, Name = "КН-31", OwnerId = ownerId, Owner = owner });
        await context.SaveChangesAsync();

        SetUser(controller, ownerId);


        var result = await controller.GetAll();


        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var groups = ok.Value as List<GroupSummaryDto>;
        groups.Should().HaveCount(1);
        groups!.First().OwnerName.Should().Be("starosta");
        groups.First().MemberCount.Should().Be(2);
    }

    [Fact]
    public async Task GetMine_ShouldReturnNotFound_WhenUserHasNoGroup()
    {

        var context = GetDbContext();
        var controller = new GroupController(context);
        var userId = Guid.NewGuid();

        context.Users.Add(new User { Id = userId, Username = "lonely" });
        await context.SaveChangesAsync();

        SetUser(controller, userId);


        var result = await controller.GetMine();


        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task GetMine_ShouldReturnGroupWithMembers()
    {

        var context = GetDbContext();
        var controller = new GroupController(context);
        var ownerId = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        var groupId = Guid.NewGuid();

        var owner = new User { Id = ownerId, Username = "starosta", GroupId = groupId };
        var member = new User { Id = memberId, Username = "student", GroupId = groupId };
        context.Users.AddRange(owner, member);
        context.Groups.Add(new Group { Id = groupId, Name = "КН-31", OwnerId = ownerId });
        await context.SaveChangesAsync();

        SetUser(controller, memberId);


        var result = await controller.GetMine();


        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var details = ok.Value as GroupDetailsDto;
        details.Should().NotBeNull();
        details!.OwnerId.Should().Be(ownerId);
        details.Members.Should().HaveCount(2);
        details.Members.First(m => m.Id == ownerId).IsOwner.Should().BeTrue();
        details.Members.First(m => m.Id == memberId).IsOwner.Should().BeFalse();
    }

    [Fact]
    public async Task Leave_ShouldReturnBadRequest_WhenOwner()
    {

        var context = GetDbContext();
        var controller = new GroupController(context);
        var ownerId = Guid.NewGuid();
        var groupId = Guid.NewGuid();

        context.Users.Add(new User { Id = ownerId, Username = "starosta", GroupId = groupId });
        context.Groups.Add(new Group { Id = groupId, Name = "КН-31", OwnerId = ownerId });
        await context.SaveChangesAsync();

        SetUser(controller, ownerId);


        var result = await controller.Leave();


        result.Should().BeOfType<BadRequestObjectResult>();
        var user = await context.Users.FindAsync(ownerId);
        user!.GroupId.Should().Be(groupId, "староста не має виходити");
    }

    [Fact]
    public async Task Leave_ShouldSucceed_WhenRegularMember()
    {

        var context = GetDbContext();
        var controller = new GroupController(context);
        var memberId = Guid.NewGuid();
        var groupId = Guid.NewGuid();

        context.Users.Add(new User { Id = memberId, Username = "student", GroupId = groupId });
        context.Groups.Add(new Group { Id = groupId, Name = "КН-31", OwnerId = Guid.NewGuid() });
        await context.SaveChangesAsync();

        SetUser(controller, memberId);


        var result = await controller.Leave();


        result.Should().BeOfType<OkObjectResult>();
        var user = await context.Users.FindAsync(memberId);
        user!.GroupId.Should().BeNull();
    }

    [Fact]
    public async Task Kick_ShouldReturnForbid_WhenNotOwner()
    {

        var context = GetDbContext();
        var controller = new GroupController(context);
        var memberId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var groupId = Guid.NewGuid();

        context.Users.Add(new User { Id = memberId, Username = "student", GroupId = groupId });
        context.Users.Add(new User { Id = targetId, Username = "інший", GroupId = groupId });
        context.Groups.Add(new Group { Id = groupId, Name = "КН-31", OwnerId = Guid.NewGuid() });
        await context.SaveChangesAsync();

        SetUser(controller, memberId);


        var result = await controller.Kick(targetId);


        result.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task Kick_ShouldRemoveUserFromGroup_WhenOwner()
    {

        var context = GetDbContext();
        var controller = new GroupController(context);
        var ownerId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var groupId = Guid.NewGuid();

        context.Users.Add(new User { Id = ownerId, Username = "starosta", GroupId = groupId });
        context.Users.Add(new User { Id = targetId, Username = "винний", GroupId = groupId });
        context.Groups.Add(new Group { Id = groupId, Name = "КН-31", OwnerId = ownerId });
        await context.SaveChangesAsync();

        SetUser(controller, ownerId);


        var result = await controller.Kick(targetId);


        result.Should().BeOfType<OkObjectResult>();
        var target = await context.Users.FindAsync(targetId);
        target!.GroupId.Should().BeNull();
    }

    [Fact]
    public async Task Kick_ShouldReturnBadRequest_WhenSelfKick()
    {

        var context = GetDbContext();
        var controller = new GroupController(context);
        var ownerId = Guid.NewGuid();
        var groupId = Guid.NewGuid();

        context.Users.Add(new User { Id = ownerId, Username = "starosta", GroupId = groupId });
        context.Groups.Add(new Group { Id = groupId, Name = "КН-31", OwnerId = ownerId });
        await context.SaveChangesAsync();

        SetUser(controller, ownerId);


        var result = await controller.Kick(ownerId);


        result.Should().BeOfType<BadRequestObjectResult>();
    }
}
