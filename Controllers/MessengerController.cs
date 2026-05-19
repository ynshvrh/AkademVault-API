using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using AkademVault_API.Data;
using AkademVault_API.Hubs;
using AkademVault_API.Models;
using System.Security.Claims;

namespace AkademVault_API.Controllers;

// REST surface for the group chat: history paging, message deletion, read-receipts (sending lives in ChatHub).
[Authorize]
[ApiController]
[Route("api/[controller]")]
public class MessengerController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly IHubContext<ChatHub> _chatHub;

    public MessengerController(AppDbContext context, IHubContext<ChatHub> chatHub)
    {
        _context = context;
        _chatHub = chatHub;
    }


    // Paged chat history (newest first) so the SPA can lazy-load older messages on scroll.
    [HttpGet("history")]
    public async Task<IActionResult> GetHistory([FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        if (page < 1) page = 1;
        if (pageSize < 1 || pageSize > 200) pageSize = 50;

        var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        var user = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);

        if (user?.GroupId == null)
            return BadRequest(new { message = "Ви не належите до жодної групи." });

        var messages = await _context.ChatMessages
            .AsNoTracking()
            .Where(m => m.GroupId == user.GroupId)
            .OrderByDescending(m => m.SentAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(m => new ChatMessageDto(
                m.Id,
                m.SenderId,
                m.Sender!.Username,
                m.Content,
                m.SentAt,
                m.Reads.Select(r => r.UserId).ToList()))
            .ToListAsync();

        return Ok(messages);
    }


    // Author-or-Owner-only: removes a message; clients pick up the deletion on their next history fetch.
    [HttpDelete("messages/{id}")]
    public async Task<IActionResult> DeleteMessage(Guid id)
    {
        var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        var message = await _context.ChatMessages.Include(m => m.Group).FirstOrDefaultAsync(m => m.Id == id);

        if (message == null) return NotFound(new { message = "Повідомлення не знайдено." });


        if (message.SenderId != userId && message.Group?.OwnerId != userId)
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "Видалити може тільки автор або староста групи." });

        _context.ChatMessages.Remove(message);
        await _context.SaveChangesAsync();
        return Ok(new { message = "Повідомлення видалено" });
    }


    // Marks one message as read by the caller and pushes a SignalR receipt to the group.
    [HttpPost("messages/{id}/read")]
    public async Task<IActionResult> MarkRead(Guid id)
    {
        var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        var user = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
        if (user?.GroupId == null)
            return BadRequest(new { message = "Ви не належите до жодної групи." });

        var message = await _context.ChatMessages.AsNoTracking().FirstOrDefaultAsync(m => m.Id == id);
        if (message == null) return NotFound(new { message = "Повідомлення не знайдено." });
        if (message.GroupId != user.GroupId)
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "Повідомлення з іншої групи." });

        // Authors don't mark their own messages — they're implicitly read.
        if (message.SenderId == userId) return Ok();

        var exists = await _context.MessageReads.AnyAsync(r => r.MessageId == id && r.UserId == userId);
        if (!exists)
        {
            _context.MessageReads.Add(new MessageRead { MessageId = id, UserId = userId, ReadAt = DateTime.UtcNow });
            await _context.SaveChangesAsync();

            await _chatHub.Clients.Group(user.GroupId.ToString()!).SendAsync("MessageRead", new
            {
                messageId = id,
                userId = userId,
                readAt = DateTime.UtcNow
            });
        }

        return Ok();
    }


    // Marks many messages as read in one round-trip; broadcasts a single MessagesRead receipt.
    [HttpPost("messages/read-batch")]
    public async Task<IActionResult> MarkReadBatch([FromBody] BatchReadRequest req)
    {
        if (req?.MessageIds == null || req.MessageIds.Count == 0)
            return Ok(new { marked = 0 });

        var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        var user = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
        if (user?.GroupId == null)
            return BadRequest(new { message = "Ви не належите до жодної групи." });

        var ids = req.MessageIds.Distinct().ToList();
        var candidates = await _context.ChatMessages
            .AsNoTracking()
            .Where(m => ids.Contains(m.Id) && m.GroupId == user.GroupId && m.SenderId != userId)
            .Select(m => m.Id)
            .ToListAsync();

        if (candidates.Count == 0) return Ok(new { marked = 0 });

        var alreadyRead = await _context.MessageReads
            .Where(r => candidates.Contains(r.MessageId) && r.UserId == userId)
            .Select(r => r.MessageId)
            .ToListAsync();
        var toInsert = candidates.Except(alreadyRead).ToList();
        if (toInsert.Count == 0) return Ok(new { marked = 0 });

        var now = DateTime.UtcNow;
        _context.MessageReads.AddRange(toInsert.Select(mid => new MessageRead
        {
            MessageId = mid,
            UserId = userId,
            ReadAt = now
        }));
        await _context.SaveChangesAsync();

        await _chatHub.Clients.Group(user.GroupId.ToString()!).SendAsync("MessagesRead", new
        {
            messageIds = toInsert,
            userId = userId,
            readAt = now
        });

        return Ok(new { marked = toInsert.Count });
    }
}

// Chat-message row returned by GET /messenger/history; ReadByUserIds powers the per-user read indicator.
public record ChatMessageDto(
    Guid Id,
    Guid SenderId,
    string SenderName,
    string Content,
    DateTime SentAt,
    List<Guid> ReadByUserIds);

// Request body for POST /messenger/messages/read-batch.
public class BatchReadRequest
{
    public List<Guid> MessageIds { get; set; } = new();
}
