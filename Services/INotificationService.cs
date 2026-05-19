using AkademVault_API.Models;

namespace AkademVault_API.Services;

// Persists notifications and pushes them through SignalR; abstracted to allow test fakes.
public interface INotificationService
{
    // Sends a notification to a single user.
    Task NotifyAsync(Guid userId, NotificationType type, string title, string body, Guid? relatedEntityId = null, CancellationToken ct = default);

    // Fans out the same notification to many users in one DB roundtrip.
    Task NotifyManyAsync(IEnumerable<Guid> userIds, NotificationType type, string title, string body, Guid? relatedEntityId = null, CancellationToken ct = default);
}
