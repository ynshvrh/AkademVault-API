using Xunit;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using AkademVault_API.Controllers;
using AkademVault_API.Data;
using AkademVault_API.Models;
using AkademVault_API.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;

namespace Tests;

public class InvitationTests
{
    private AppDbContext GetDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    private static void SetUser(InvitationController controller, Guid userId)
    {
        var claims = new List<Claim> { new Claim(ClaimTypes.NameIdentifier, userId.ToString()) };
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims)) }
        };
    }

    [Fact]
    public async Task Send_ShouldReturnForbid_WhenNotOwner()
    {
        var context = GetDbContext();
        var notif = new FakeNotificationService();
        var controller = new InvitationController(context, notif);

        var userId = Guid.NewGuid();
        context.Users.Add(new User { Id = userId, Username = "student" });
        await context.SaveChangesAsync();

        SetUser(controller, userId);

        var result = await controller.Send(new SendInvitationRequest { UsernameOrEmail = "anyone" });
        var __fr = result.Should().BeOfType<ObjectResult>().Subject; __fr.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    [Fact]
    public async Task Send_ShouldCreateInvitationAndNotify()
    {
        var context = GetDbContext();
        var notif = new FakeNotificationService();
        var controller = new InvitationController(context, notif);

        var ownerId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var groupId = Guid.NewGuid();

        context.Groups.Add(new Group { Id = groupId, Name = "КН-31", ShortCode = "TST-0001", OwnerId = ownerId });
        context.Users.Add(new User { Id = ownerId, Username = "starosta", GroupId = groupId });
        context.Users.Add(new User { Id = targetId, Username = "newbie", Email = "n@u.ua" });
        await context.SaveChangesAsync();

        SetUser(controller, ownerId);

        var result = await controller.Send(new SendInvitationRequest { UsernameOrEmail = "newbie" });

        result.Should().BeOfType<OkObjectResult>();
        (await context.Invitations.CountAsync()).Should().Be(1);
        notif.Sent.Should().ContainSingle()
            .Which.Type.Should().Be(NotificationType.GroupInvitation);
    }

    [Fact]
    public async Task Send_ShouldReturnBadRequest_WhenAlreadyInGroup()
    {
        var context = GetDbContext();
        var notif = new FakeNotificationService();
        var controller = new InvitationController(context, notif);

        var ownerId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var groupId = Guid.NewGuid();

        context.Groups.Add(new Group { Id = groupId, Name = "КН-31", ShortCode = "TST-0001", OwnerId = ownerId });
        context.Users.Add(new User { Id = ownerId, Username = "starosta", GroupId = groupId });
        context.Users.Add(new User { Id = targetId, Username = "вже_тут", GroupId = groupId });
        await context.SaveChangesAsync();

        SetUser(controller, ownerId);

        var result = await controller.Send(new SendInvitationRequest { UsernameOrEmail = "вже_тут" });

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task Accept_ShouldJoinUserToGroup()
    {
        var context = GetDbContext();
        var notif = new FakeNotificationService();
        var controller = new InvitationController(context, notif);

        var ownerId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        var invitationId = Guid.NewGuid();

        context.Groups.Add(new Group { Id = groupId, Name = "КН-31", ShortCode = "TST-0001", OwnerId = ownerId });
        context.Users.Add(new User { Id = targetId, Username = "newbie" });
        context.Invitations.Add(new Invitation
        {
            Id = invitationId,
            GroupId = groupId,
            InvitedUserId = targetId,
            InvitedByUserId = ownerId,
            Status = InvitationStatus.Pending
        });
        await context.SaveChangesAsync();

        SetUser(controller, targetId);

        var result = await controller.Accept(invitationId);

        result.Should().BeOfType<OkObjectResult>();
        (await context.Users.FindAsync(targetId))!.GroupId.Should().Be(groupId);
        (await context.Invitations.FindAsync(invitationId))!.Status.Should().Be(InvitationStatus.Accepted);
    }

    [Fact]
    public async Task Accept_ShouldReturnBadRequest_WhenAlreadyInGroup()
    {
        var context = GetDbContext();
        var notif = new FakeNotificationService();
        var controller = new InvitationController(context, notif);

        var targetId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        var anotherGroupId = Guid.NewGuid();
        var invitationId = Guid.NewGuid();

        context.Groups.Add(new Group { Id = groupId, Name = "КН-31", ShortCode = "TST-0001", OwnerId = Guid.NewGuid() });
        context.Users.Add(new User { Id = targetId, Username = "newbie", GroupId = anotherGroupId });
        context.Invitations.Add(new Invitation
        {
            Id = invitationId,
            GroupId = groupId,
            InvitedUserId = targetId,
            InvitedByUserId = Guid.NewGuid(),
            Status = InvitationStatus.Pending
        });
        await context.SaveChangesAsync();

        SetUser(controller, targetId);

        var result = await controller.Accept(invitationId);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task Decline_ShouldSetStatus()
    {
        var context = GetDbContext();
        var notif = new FakeNotificationService();
        var controller = new InvitationController(context, notif);

        var targetId = Guid.NewGuid();
        var invitationId = Guid.NewGuid();

        context.Users.Add(new User { Id = targetId, Username = "newbie" });
        context.Invitations.Add(new Invitation
        {
            Id = invitationId,
            GroupId = Guid.NewGuid(),
            InvitedUserId = targetId,
            InvitedByUserId = Guid.NewGuid(),
            Status = InvitationStatus.Pending
        });
        await context.SaveChangesAsync();

        SetUser(controller, targetId);

        var result = await controller.Decline(invitationId);

        result.Should().BeOfType<OkObjectResult>();
        (await context.Invitations.FindAsync(invitationId))!.Status.Should().Be(InvitationStatus.Declined);
    }

    [Fact]
    public async Task CreateLink_ShouldGenerateLinkWithFutureExpiry()
    {
        var context = GetDbContext();
        var notif = new FakeNotificationService();
        var controller = new InvitationController(context, notif);

        var ownerId = Guid.NewGuid();
        context.Groups.Add(new Group { Id = Guid.NewGuid(), Name = "КН-31", ShortCode = "TST-0001", OwnerId = ownerId });
        await context.SaveChangesAsync();

        SetUser(controller, ownerId);

        var result = await controller.CreateLink();

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var dto = ok.Value as InviteLinkDto;
        dto.Should().NotBeNull();
        dto!.Token.Should().NotBeNullOrEmpty();
        dto.ExpiresAt.Should().BeAfter(DateTime.UtcNow.AddDays(29));
    }

    [Fact]
    public async Task AcceptLink_ShouldJoinUser()
    {
        var context = GetDbContext();
        var notif = new FakeNotificationService();
        var controller = new InvitationController(context, notif);

        var userId = Guid.NewGuid();
        var groupId = Guid.NewGuid();

        context.Groups.Add(new Group { Id = groupId, Name = "КН-31", ShortCode = "TST-0001", OwnerId = Guid.NewGuid() });
        context.Users.Add(new User { Id = userId, Username = "newbie" });
        context.GroupInviteLinks.Add(new GroupInviteLink
        {
            Id = Guid.NewGuid(),
            GroupId = groupId,
            CreatedByUserId = Guid.NewGuid(),
            Token = "valid-token",
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(15)
        });
        await context.SaveChangesAsync();

        SetUser(controller, userId);

        var result = await controller.AcceptLink("valid-token");

        result.Should().BeOfType<OkObjectResult>();
        (await context.Users.FindAsync(userId))!.GroupId.Should().Be(groupId);
    }

    [Fact]
    public async Task AcceptLink_ShouldReturnBadRequest_WhenRevoked()
    {
        var context = GetDbContext();
        var notif = new FakeNotificationService();
        var controller = new InvitationController(context, notif);

        var userId = Guid.NewGuid();
        context.Users.Add(new User { Id = userId, Username = "newbie" });
        context.Groups.Add(new Group { Id = Guid.NewGuid(), Name = "G", ShortCode = "TST-0001", OwnerId = Guid.NewGuid() });
        context.GroupInviteLinks.Add(new GroupInviteLink
        {
            Id = Guid.NewGuid(),
            GroupId = Guid.NewGuid(),
            CreatedByUserId = Guid.NewGuid(),
            Token = "revoked",
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(15),
            RevokedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync();

        SetUser(controller, userId);

        var result = await controller.AcceptLink("revoked");
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task AcceptLink_ShouldReturnBadRequest_WhenExpired()
    {
        var context = GetDbContext();
        var notif = new FakeNotificationService();
        var controller = new InvitationController(context, notif);

        var userId = Guid.NewGuid();
        context.Users.Add(new User { Id = userId, Username = "newbie" });
        context.GroupInviteLinks.Add(new GroupInviteLink
        {
            Id = Guid.NewGuid(),
            GroupId = Guid.NewGuid(),
            CreatedByUserId = Guid.NewGuid(),
            Token = "expired",
            CreatedAt = DateTime.UtcNow.AddDays(-60),
            ExpiresAt = DateTime.UtcNow.AddDays(-30)
        });
        await context.SaveChangesAsync();

        SetUser(controller, userId);

        var result = await controller.AcceptLink("expired");
        result.Should().BeOfType<BadRequestObjectResult>();
    }
}
