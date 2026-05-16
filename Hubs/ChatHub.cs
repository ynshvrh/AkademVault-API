using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using AkademVault_API.Data;
using AkademVault_API.Models;
using System.Security.Claims;

namespace AkademVault_API.Hubs;

[Authorize]
public class ChatHub : Hub
{
    private readonly AppDbContext _context;

    public ChatHub(AppDbContext context) => _context = context;


    public override async Task OnConnectedAsync()
    {
        var userId = Guid.Parse(Context.User!.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        var user = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);

        if (user?.GroupId == null)
        {
            Context.Abort();
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, user.GroupId.ToString()!);
        await base.OnConnectedAsync();
    }


    public async Task SendMessage(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            throw new HubException("Повідомлення не може бути порожнім");

        if (content.Length > 2000)
            throw new HubException("Повідомлення занадто довге");

        var userId = Guid.Parse(Context.User!.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        var user = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);

        if (user?.GroupId == null)
            throw new HubException("Ви не належите до жодної групи");

        var message = new ChatMessage
        {
            Id = Guid.NewGuid(),
            GroupId = user.GroupId.Value,
            SenderId = userId,
            Content = content.Trim(),
            SentAt = DateTime.UtcNow
        };

        _context.ChatMessages.Add(message);
        await _context.SaveChangesAsync();

        await Clients.Group(user.GroupId.ToString()!).SendAsync("ReceiveMessage", new
        {
            id = message.Id,
            senderId = message.SenderId,
            senderName = user.Username,
            content = message.Content,
            sentAt = message.SentAt
        });
    }
}
