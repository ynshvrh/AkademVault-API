using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AkademVault_API.Data;
using AkademVault_API.Models;
using System.Security.Claims;

namespace AkademVault_API.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class NotificationController : ControllerBase
{
    private readonly AppDbContext _context;

    public NotificationController(AppDbContext context) => _context = context;


    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] bool onlyUnread = false, [FromQuery] int limit = 50)
    {
        if (limit < 1 || limit > 200) limit = 50;
        var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

        var query = _context.Notifications.AsNoTracking().Where(n => n.UserId == userId);
        if (onlyUnread) query = query.Where(n => !n.IsRead);

        var items = await query
            .OrderByDescending(n => n.CreatedAt)
            .Take(limit)
            .Select(n => new NotificationDto(
                n.Id,
                n.Type.ToString(),
                n.Title,
                n.Body,
                n.RelatedEntityId,
                n.IsRead,
                n.CreatedAt))
            .ToListAsync();

        var unreadCount = await _context.Notifications
            .AsNoTracking()
            .CountAsync(n => n.UserId == userId && !n.IsRead);

        return Ok(new { unreadCount, items });
    }


    [HttpPost("{id}/read")]
    public async Task<IActionResult> MarkRead(Guid id)
    {
        var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        var notif = await _context.Notifications.FirstOrDefaultAsync(n => n.Id == id && n.UserId == userId);
        if (notif == null) return NotFound(new { message = "Нотифікацію не знайдено." });

        if (!notif.IsRead)
        {
            notif.IsRead = true;
            await _context.SaveChangesAsync();
        }
        return Ok();
    }


    [HttpPost("read-all")]
    public async Task<IActionResult> MarkAllRead()
    {
        var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        var unread = await _context.Notifications.Where(n => n.UserId == userId && !n.IsRead).ToListAsync();

        foreach (var n in unread) n.IsRead = true;
        await _context.SaveChangesAsync();
        return Ok(new { marked = unread.Count });
    }


    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        var notif = await _context.Notifications.FirstOrDefaultAsync(n => n.Id == id && n.UserId == userId);
        if (notif == null) return NotFound(new { message = "Нотифікацію не знайдено." });

        _context.Notifications.Remove(notif);
        await _context.SaveChangesAsync();
        return Ok();
    }
}

public record NotificationDto(
    Guid Id,
    string Type,
    string Title,
    string Body,
    Guid? RelatedEntityId,
    bool IsRead,
    DateTime CreatedAt);
