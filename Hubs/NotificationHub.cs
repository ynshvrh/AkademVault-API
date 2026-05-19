using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using System.Security.Claims;

namespace AkademVault_API.Hubs;

// SignalR hub for per-user notification pushes; clients join a SignalR group named "user:{userId}".
[Authorize]
public class NotificationHub : Hub
{
    // Adds the connection to its owner's user-scoped SignalR group so NotificationService can target it.
    public override async Task OnConnectedAsync()
    {
        var userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            Context.Abort();
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, $"user:{userId}");
        await base.OnConnectedAsync();
    }
}
