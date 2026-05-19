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


// INotificationService fake: records each call so tests can assert on the fan-out behaviour.
public class FakeNotificationService : INotificationService
{
    public List<(Guid UserId, NotificationType Type, string Title, string Body, Guid? Related)> Sent { get; } = new();

    public Task NotifyAsync(Guid userId, NotificationType type, string title, string body, Guid? relatedEntityId = null, CancellationToken ct = default)
    {
        Sent.Add((userId, type, title, body, relatedEntityId));
        return Task.CompletedTask;
    }

    public Task NotifyManyAsync(IEnumerable<Guid> userIds, NotificationType type, string title, string body, Guid? relatedEntityId = null, CancellationToken ct = default)
    {
        foreach (var uid in userIds.Distinct())
            Sent.Add((uid, type, title, body, relatedEntityId));
        return Task.CompletedTask;
    }
}

// Tests for the notification inbox endpoints (list, mark-read, mark-all, delete) and scoping by user.
public class NotificationTests
{
    private AppDbContext GetDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    private static void SetUser(NotificationController controller, Guid userId)
    {
        var claims = new List<Claim> { new Claim(ClaimTypes.NameIdentifier, userId.ToString()) };
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims)) }
        };
    }

    // GET /notification returns only notifications owned by the caller plus a correct unreadCount.
    [Fact]
    public async Task GetAll_ShouldReturnOnlyOwnNotifications()
    {
        var context = GetDbContext();
        var controller = new NotificationController(context);
        var meId = Guid.NewGuid();
        var otherId = Guid.NewGuid();

        context.Notifications.Add(new Notification { Id = Guid.NewGuid(), UserId = meId, Type = NotificationType.MentionInChat, Title = "моє", Body = "...", CreatedAt = DateTime.UtcNow });
        context.Notifications.Add(new Notification { Id = Guid.NewGuid(), UserId = otherId, Type = NotificationType.MentionInChat, Title = "чуже", Body = "...", CreatedAt = DateTime.UtcNow });
        await context.SaveChangesAsync();

        SetUser(controller, meId);

        var result = await controller.GetAll();

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var unreadCount = (int)ok.Value!.GetType().GetProperty("unreadCount")!.GetValue(ok.Value)!;
        var items = (IEnumerable<NotificationDto>)ok.Value.GetType().GetProperty("items")!.GetValue(ok.Value)!;

        items.Should().HaveCount(1);
        items.First().Title.Should().Be("моє");
        unreadCount.Should().Be(1);
    }

    // ?onlyUnread=true filters out already-read notifications.
    [Fact]
    public async Task GetAll_OnlyUnread_FiltersReadNotifications()
    {
        var context = GetDbContext();
        var controller = new NotificationController(context);
        var meId = Guid.NewGuid();

        context.Notifications.Add(new Notification { Id = Guid.NewGuid(), UserId = meId, Type = NotificationType.MentionInChat, Title = "А", Body = ".", IsRead = true });
        context.Notifications.Add(new Notification { Id = Guid.NewGuid(), UserId = meId, Type = NotificationType.MentionInChat, Title = "Б", Body = ".", IsRead = false });
        await context.SaveChangesAsync();

        SetUser(controller, meId);

        var result = await controller.GetAll(onlyUnread: true);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var items = (IEnumerable<NotificationDto>)ok.Value!.GetType().GetProperty("items")!.GetValue(ok.Value)!;

        items.Should().HaveCount(1);
        items.First().Title.Should().Be("Б");
    }

    // MarkRead flips IsRead=true for the caller's own notification.
    [Fact]
    public async Task MarkRead_ShouldFlipIsRead()
    {
        var context = GetDbContext();
        var controller = new NotificationController(context);
        var meId = Guid.NewGuid();
        var notifId = Guid.NewGuid();

        context.Notifications.Add(new Notification { Id = notifId, UserId = meId, Type = NotificationType.MentionInChat, Title = "А", Body = ".", IsRead = false });
        await context.SaveChangesAsync();

        SetUser(controller, meId);

        var result = await controller.MarkRead(notifId);

        result.Should().BeOfType<OkResult>();
        (await context.Notifications.FindAsync(notifId))!.IsRead.Should().BeTrue();
    }

    // Trying to mark another user's notification as read returns 404 (not 403, to avoid leaking existence).
    [Fact]
    public async Task MarkRead_ShouldReturnNotFound_ForOtherUsersNotification()
    {
        var context = GetDbContext();
        var controller = new NotificationController(context);
        var meId = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        var notifId = Guid.NewGuid();

        context.Notifications.Add(new Notification { Id = notifId, UserId = otherId, Type = NotificationType.MentionInChat, Title = "чуже", Body = "." });
        await context.SaveChangesAsync();

        SetUser(controller, meId);

        var result = await controller.MarkRead(notifId);

        result.Should().BeOfType<NotFoundObjectResult>();
    }

    // MarkAllRead only touches the caller's unread rows; other users' notifications stay unread.
    [Fact]
    public async Task MarkAllRead_ShouldMarkOnlyOwnUnread()
    {
        var context = GetDbContext();
        var controller = new NotificationController(context);
        var meId = Guid.NewGuid();
        var otherId = Guid.NewGuid();

        context.Notifications.Add(new Notification { Id = Guid.NewGuid(), UserId = meId, Type = NotificationType.MentionInChat, Title = "А", Body = ".", IsRead = false });
        context.Notifications.Add(new Notification { Id = Guid.NewGuid(), UserId = meId, Type = NotificationType.MentionInChat, Title = "Б", Body = ".", IsRead = false });
        context.Notifications.Add(new Notification { Id = Guid.NewGuid(), UserId = otherId, Type = NotificationType.MentionInChat, Title = "чужа", Body = ".", IsRead = false });
        await context.SaveChangesAsync();

        SetUser(controller, meId);

        var result = await controller.MarkAllRead();

        result.Should().BeOfType<OkObjectResult>();
        (await context.Notifications.CountAsync(n => n.UserId == meId && !n.IsRead)).Should().Be(0);
        (await context.Notifications.CountAsync(n => n.UserId == otherId && !n.IsRead)).Should().Be(1, "чужі нотифікації не чіпаємо");
    }
}
