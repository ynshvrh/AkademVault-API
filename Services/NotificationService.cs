using Microsoft.AspNetCore.SignalR;
using AkademVault_API.Data;
using AkademVault_API.Hubs;
using AkademVault_API.Models;

namespace AkademVault_API.Services;

// Concrete INotificationService: writes to the Notifications table and broadcasts via NotificationHub.
public class NotificationService : INotificationService
{
    private readonly AppDbContext _context;
    private readonly IHubContext<NotificationHub> _hub;

    public NotificationService(AppDbContext context, IHubContext<NotificationHub> hub)
    {
        _context = context;
        _hub = hub;
    }

    // Inserts one Notification row and emits ReceiveNotification on the user-scoped SignalR group.
    public async Task NotifyAsync(Guid userId, NotificationType type, string title, string body, Guid? relatedEntityId = null, CancellationToken ct = default)
    {
        var notif = new Notification
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Type = type,
            Title = Truncate(title, 200),
            Body = Truncate(body, 500),
            RelatedEntityId = relatedEntityId,
            IsRead = false,
            CreatedAt = DateTime.UtcNow
        };

        _context.Notifications.Add(notif);
        await _context.SaveChangesAsync(ct);

        await _hub.Clients.Group($"user:{userId}").SendAsync("ReceiveNotification", new
        {
            id = notif.Id,
            type = notif.Type.ToString(),
            title = notif.Title,
            body = notif.Body,
            relatedEntityId = notif.RelatedEntityId,
            createdAt = notif.CreatedAt
        }, ct);
    }

    // Bulk-inserts notifications and pushes one ReceiveNotification per recipient (still N hub calls).
    public async Task NotifyManyAsync(IEnumerable<Guid> userIds, NotificationType type, string title, string body, Guid? relatedEntityId = null, CancellationToken ct = default)
    {
        var ids = userIds.Distinct().ToList();
        if (ids.Count == 0) return;

        var now = DateTime.UtcNow;
        var truncatedTitle = Truncate(title, 200);
        var truncatedBody = Truncate(body, 500);

        var notifs = ids.Select(uid => new Notification
        {
            Id = Guid.NewGuid(),
            UserId = uid,
            Type = type,
            Title = truncatedTitle,
            Body = truncatedBody,
            RelatedEntityId = relatedEntityId,
            IsRead = false,
            CreatedAt = now
        }).ToList();

        _context.Notifications.AddRange(notifs);
        await _context.SaveChangesAsync(ct);

        foreach (var notif in notifs)
        {
            await _hub.Clients.Group($"user:{notif.UserId}").SendAsync("ReceiveNotification", new
            {
                id = notif.Id,
                type = notif.Type.ToString(),
                title = notif.Title,
                body = notif.Body,
                relatedEntityId = notif.RelatedEntityId,
                createdAt = notif.CreatedAt
            }, ct);
        }
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s.Substring(0, max);
}
