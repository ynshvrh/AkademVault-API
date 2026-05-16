using AkademVault_API.Models;

namespace AkademVault_API.Services;

public interface INotificationService
{
    Task NotifyAsync(Guid userId, NotificationType type, string title, string body, Guid? relatedEntityId = null, CancellationToken ct = default);
    Task NotifyManyAsync(IEnumerable<Guid> userIds, NotificationType type, string title, string body, Guid? relatedEntityId = null, CancellationToken ct = default);
}
