using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AkademVault_API.Data;
using System.Security.Claims;

namespace AkademVault_API.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class MessengerController : ControllerBase
{
    private readonly AppDbContext _context;

    public MessengerController(AppDbContext context) => _context = context;


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
                m.SentAt))
            .ToListAsync();

        return Ok(messages);
    }


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
}

public record ChatMessageDto(Guid Id, Guid SenderId, string SenderName, string Content, DateTime SentAt);
