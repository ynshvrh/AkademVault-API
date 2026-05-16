using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using AkademVault_API.Data;
using AkademVault_API.Models;
using AkademVault_API.Services;
using System.Security.Claims;
using System.Text.RegularExpressions;

namespace AkademVault_API.Hubs;

[Authorize]
public class ChatHub : Hub
{
    private static readonly Regex MentionPattern = new(@"@(\w+)", RegexOptions.Compiled);

    private readonly AppDbContext _context;
    private readonly INotificationService _notifications;

    public ChatHub(AppDbContext context, INotificationService notifications)
    {
        _context = context;
        _notifications = notifications;
    }


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

        var trimmed = content.Trim();
        var message = new ChatMessage
        {
            Id = Guid.NewGuid(),
            GroupId = user.GroupId.Value,
            SenderId = userId,
            Content = trimmed,
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


        var usernames = MentionPattern.Matches(trimmed)
            .Select(m => m.Groups[1].Value)
            .Where(u => !string.Equals(u, user.Username, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (usernames.Count > 0)
        {
            var mentioned = await _context.Users
                .AsNoTracking()
                .Where(u => u.GroupId == user.GroupId && usernames.Contains(u.Username) && u.Id != userId)
                .Select(u => u.Id)
                .ToListAsync();

            if (mentioned.Count > 0)
            {
                await _notifications.NotifyManyAsync(
                    mentioned,
                    NotificationType.MentionInChat,
                    $"{user.Username} згадав вас у чаті",
                    trimmed.Length > 200 ? trimmed.Substring(0, 200) + "…" : trimmed,
                    message.Id);
            }
        }
    }
}
