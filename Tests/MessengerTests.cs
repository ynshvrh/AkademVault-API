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

public class MessengerTests
{
    private AppDbContext GetDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    private static void SetUser(MessengerController controller, Guid userId)
    {
        var claims = new List<Claim> { new Claim(ClaimTypes.NameIdentifier, userId.ToString()) };
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims)) }
        };
    }

    [Fact]
    public async Task GetHistory_ShouldReturnBadRequest_WhenUserHasNoGroup()
    {

        var context = GetDbContext();
        var controller = new MessengerController(context);
        var userId = Guid.NewGuid();

        context.Users.Add(new User { Id = userId, Username = "samotnyak" });
        await context.SaveChangesAsync();

        SetUser(controller, userId);


        var result = await controller.GetHistory();


        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task GetHistory_ShouldReturnOnlyGroupMessages()
    {

        var context = GetDbContext();
        var controller = new MessengerController(context);
        var groupId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();

        context.Users.Add(new User { Id = userId, GroupId = groupId, Username = "yanosh_dev" });
        context.Users.Add(new User { Id = otherUserId, Username = "stranger" });


        context.ChatMessages.Add(new ChatMessage
        {
            Id = Guid.NewGuid(),
            GroupId = groupId,
            SenderId = userId,
            Content = "Привіт групі",
            SentAt = DateTime.UtcNow
        });


        context.ChatMessages.Add(new ChatMessage
        {
            Id = Guid.NewGuid(),
            GroupId = Guid.NewGuid(),
            SenderId = otherUserId,
            Content = "Чуже повідомлення",
            SentAt = DateTime.UtcNow
        });

        await context.SaveChangesAsync();

        SetUser(controller, userId);


        var result = await controller.GetHistory();


        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var messages = okResult.Value as List<ChatMessageDto>;
        messages.Should().NotBeNull();
        messages!.Should().HaveCount(1);
        messages.First().Content.Should().Be("Привіт групі");
    }

    [Fact]
    public async Task GetHistory_ShouldReturnMessagesOrderedByNewestFirst()
    {

        var context = GetDbContext();
        var controller = new MessengerController(context);
        var groupId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        context.Users.Add(new User { Id = userId, GroupId = groupId, Username = "yanosh_dev" });

        var older = new ChatMessage
        {
            Id = Guid.NewGuid(),
            GroupId = groupId,
            SenderId = userId,
            Content = "Старе",
            SentAt = DateTime.UtcNow.AddMinutes(-10)
        };

        var newer = new ChatMessage
        {
            Id = Guid.NewGuid(),
            GroupId = groupId,
            SenderId = userId,
            Content = "Нове",
            SentAt = DateTime.UtcNow
        };

        context.ChatMessages.Add(older);
        context.ChatMessages.Add(newer);
        await context.SaveChangesAsync();

        SetUser(controller, userId);


        var result = await controller.GetHistory();


        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var messages = okResult.Value as List<ChatMessageDto>;
        messages.Should().NotBeNull();
        messages!.Should().HaveCount(2);
        messages.First().Id.Should().Be(newer.Id);
    }

    [Fact]
    public async Task DeleteMessage_ShouldReturnForbid_WhenUserIsNotSenderOrOwner()
    {

        var context = GetDbContext();
        var controller = new MessengerController(context);
        var groupId = Guid.NewGuid();
        var senderId = Guid.NewGuid();
        var strangerId = Guid.NewGuid();

        var group = new Group { Id = groupId, Name = "КН-31", OwnerId = Guid.NewGuid() };
        var message = new ChatMessage
        {
            Id = Guid.NewGuid(),
            GroupId = groupId,
            Group = group,
            SenderId = senderId,
            Content = "Секрет"
        };

        context.Groups.Add(group);
        context.ChatMessages.Add(message);
        await context.SaveChangesAsync();

        SetUser(controller, strangerId);


        var result = await controller.DeleteMessage(message.Id);


        result.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task DeleteMessage_ShouldReturnOk_WhenUserIsSender()
    {

        var context = GetDbContext();
        var controller = new MessengerController(context);
        var groupId = Guid.NewGuid();
        var senderId = Guid.NewGuid();

        var group = new Group { Id = groupId, Name = "КН-31", OwnerId = Guid.NewGuid() };
        var message = new ChatMessage
        {
            Id = Guid.NewGuid(),
            GroupId = groupId,
            Group = group,
            SenderId = senderId,
            Content = "Моє повідомлення"
        };

        context.Groups.Add(group);
        context.ChatMessages.Add(message);
        await context.SaveChangesAsync();

        SetUser(controller, senderId);


        var result = await controller.DeleteMessage(message.Id);


        result.Should().BeOfType<OkObjectResult>();
        context.ChatMessages.Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteMessage_ShouldReturnOk_WhenUserIsGroupOwner()
    {

        var context = GetDbContext();
        var controller = new MessengerController(context);
        var groupId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var senderId = Guid.NewGuid();

        var group = new Group { Id = groupId, Name = "КН-31", OwnerId = ownerId };
        var message = new ChatMessage
        {
            Id = Guid.NewGuid(),
            GroupId = groupId,
            Group = group,
            SenderId = senderId,
            Content = "Чуже, але я староста"
        };

        context.Groups.Add(group);
        context.ChatMessages.Add(message);
        await context.SaveChangesAsync();

        SetUser(controller, ownerId);


        var result = await controller.DeleteMessage(message.Id);


        result.Should().BeOfType<OkObjectResult>();
        context.ChatMessages.Should().BeEmpty();
    }
}
