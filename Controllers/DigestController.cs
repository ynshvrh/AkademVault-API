using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AkademVault_API.Data;
using AkademVault_API.Models;
using AkademVault_API.Services;
using System.Security.Claims;
using System.Text;

namespace AkademVault_API.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class DigestController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly IDigestAIClient _ai;
    private readonly INotificationService _notifications;

    private const string SystemPrompt =
        "Ти асистент академічної групи. Тобі дають журнал подій за певний період " +
        "(нові матеріали, зміни розкладу, чат). Зроби короткий дайджест українською мовою: " +
        "3-6 пунктів, без води, без вступів і висновків, тільки факти що сталися. " +
        "Якщо подій немає — так і скажи одним реченням.";

    public DigestController(AppDbContext context, IDigestAIClient ai, INotificationService notifications)
    {
        _context = context;
        _ai = ai;
        _notifications = notifications;
    }


    [HttpGet]
    public async Task<IActionResult> Generate([FromQuery] string period = "day", CancellationToken ct = default)
    {
        TimeSpan window = period.ToLowerInvariant() switch
        {
            "hour" => TimeSpan.FromHours(1),
            "day"  => TimeSpan.FromDays(1),
            _ => TimeSpan.Zero
        };

        if (window == TimeSpan.Zero)
            return BadRequest(new { message = "Параметр period має бути 'hour' або 'day'." });

        var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        var group = await _context.Groups.AsNoTracking().FirstOrDefaultAsync(g => g.OwnerId == userId, ct);

        if (group == null)
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "Тільки староста може запускати дайджест." });

        var since = DateTime.UtcNow - window;

        var materials = await _context.LectureMaterials
            .AsNoTracking()
            .Where(m => m.GroupId == group.Id && m.UploadedAt >= since)
            .OrderBy(m => m.UploadedAt)
            .Select(m => new { m.FileName, m.UploadedAt, Uploader = m.Uploader!.Username })
            .ToListAsync(ct);

        var assignments = await _context.Assignments
            .AsNoTracking()
            .Where(a => a.GroupId == group.Id && a.CreatedAt >= since)
            .OrderBy(a => a.CreatedAt)
            .Select(a => new { a.Title, a.Description, a.DueDate, a.CreatedAt })
            .ToListAsync(ct);

        var messages = await _context.ChatMessages
            .AsNoTracking()
            .Where(m => m.GroupId == group.Id && m.SentAt >= since)
            .OrderBy(m => m.SentAt)
            .Select(m => new { Sender = m.Sender!.Username, m.Content, m.SentAt })
            .ToListAsync(ct);

        var counts = new
        {
            materials = materials.Count,
            assignments = assignments.Count,
            messages = messages.Count
        };

        if (counts.materials == 0 && counts.assignments == 0 && counts.messages == 0)
        {
            return Ok(new
            {
                period,
                generatedAt = DateTime.UtcNow,
                counts,
                summary = "За цей період у групі не було жодної активності."
            });
        }

        var prompt = new StringBuilder();
        prompt.AppendLine($"Період: останні {(period == "hour" ? "1 година" : "24 години")} (з {since:yyyy-MM-dd HH:mm} UTC).");
        prompt.AppendLine($"Група: {group.Name}.");
        prompt.AppendLine();

        prompt.AppendLine($"## Нові матеріали ({materials.Count}):");
        foreach (var m in materials)
            prompt.AppendLine($"- {m.FileName} (від {m.Uploader}, {m.UploadedAt:HH:mm})");
        prompt.AppendLine();

        prompt.AppendLine($"## Нові завдання ({assignments.Count}):");
        foreach (var a in assignments)
            prompt.AppendLine($"- {a.Title}: {a.Description} (дедлайн {a.DueDate:yyyy-MM-dd})");
        prompt.AppendLine();

        prompt.AppendLine($"## Чат ({messages.Count} повідомлень):");
        foreach (var m in messages)
            prompt.AppendLine($"[{m.SentAt:HH:mm}] {m.Sender}: {m.Content}");

        var summary = await _ai.SummarizeAsync(SystemPrompt, prompt.ToString(), ct);


        var recipients = await _context.Users
            .AsNoTracking()
            .Where(u => u.GroupId == group.Id && u.Id != userId)
            .Select(u => u.Id)
            .ToListAsync(ct);

        if (recipients.Count > 0)
        {
            await _notifications.NotifyManyAsync(
                recipients,
                NotificationType.DigestPublished,
                $"Новий дайджест від {(period == "hour" ? "години" : "доби")}",
                $"Староста згенерував підсумок активності групи.",
                ct: ct);
        }

        return Ok(new
        {
            period,
            generatedAt = DateTime.UtcNow,
            counts,
            summary
        });
    }
}