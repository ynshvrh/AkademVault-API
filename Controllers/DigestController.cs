using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AkademVault_API.Data;
using AkademVault_API.Models;
using AkademVault_API.Services;
using System.Security.Claims;
using System.Text;
using System.Text.RegularExpressions;

namespace AkademVault_API.Controllers;

// Owner-only AI digest: collects the last 24h of group activity and asks the LLM to summarise it in Ukrainian.
[Authorize]
[ApiController]
[Route("api/[controller]")]
public class DigestController : ControllerBase
{
    private static readonly TimeSpan DigestWindow = TimeSpan.FromDays(1);

    private readonly AppDbContext _context;
    private readonly IDigestAIClient _ai;
    private readonly INotificationService _notifications;

    // Tightened prompt: bans markdown markup that was leaking into the UI as raw `**…**`,
    // and pins roles to the actual app vocabulary (the model was inventing «викладач»).
    private const string SystemPrompt =
        "Ти асистент академічної групи. Тобі дають журнал подій за останні 24 години " +
        "(нові матеріали, нові завдання, повідомлення в чаті). " +
        "Сформуй короткий дайджест українською мовою: 3-6 фактів, без води, без вступів і висновків. " +
        "ФОРМАТ: лише чистий текст без будь-якої markdown-розмітки. " +
        "Не використовуй * ** _ # ` — жодних зірочок, підкреслень, решіток, бек-тіків. " +
        "Кожен пункт починай з символу «• » (буллет + пробіл) і пиши з нового рядка. " +
        "РОЛІ: в системі є лише «староста» (Owner групи) та «одногрупники» (студенти-учасники). " +
        "Викладачів, вчителів, професорів, кураторів у системі НЕМАЄ — НЕ вживай ці слова взагалі. " +
        "Якщо подій немає — відповідай одним реченням про відсутність активності.";

    public DigestController(AppDbContext context, IDigestAIClient ai, INotificationService notifications)
    {
        _context = context;
        _ai = ai;
        _notifications = notifications;
    }


    // Returns the most recent cached digest for the caller's group (Owner-only).
    // Read-only — does NOT call the LLM. Used by the dashboard inline block.
    [HttpGet("latest")]
    public async Task<IActionResult> Latest(CancellationToken ct = default)
    {
        var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        var group = await _context.Groups.AsNoTracking()
            .FirstOrDefaultAsync(g => g.OwnerId == userId, ct);

        if (group == null)
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "Тільки староста може переглядати дайджест." });

        if (string.IsNullOrEmpty(group.LastDigestSummary) || group.LastDigestGeneratedAt == null)
            return NoContent();

        return Ok(new
        {
            generatedAt = group.LastDigestGeneratedAt,
            counts = new
            {
                materials = group.LastDigestMaterialCount ?? 0,
                assignments = group.LastDigestAssignmentCount ?? 0,
                messages = group.LastDigestMessageCount ?? 0
            },
            summary = group.LastDigestSummary
        });
    }

    // Generates a summary over the last 24h of group activity, persists it to the group row
    // (so the dashboard can read it without re-prompting the LLM) and fans out a notification.
    [HttpGet]
    public async Task<IActionResult> Generate(CancellationToken ct = default)
    {
        var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        var group = await _context.Groups.FirstOrDefaultAsync(g => g.OwnerId == userId, ct);

        if (group == null)
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "Тільки староста може запускати дайджест." });

        var since = DateTime.UtcNow - DigestWindow;

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

        // Skip the LLM round-trip when the window had no activity.
        if (counts.materials == 0 && counts.assignments == 0 && counts.messages == 0)
        {
            var emptyAt = DateTime.UtcNow;
            const string emptySummary = "За цей період у групі не було жодної активності.";
            group.LastDigestSummary = emptySummary;
            group.LastDigestGeneratedAt = emptyAt;
            group.LastDigestMaterialCount = 0;
            group.LastDigestAssignmentCount = 0;
            group.LastDigestMessageCount = 0;
            await _context.SaveChangesAsync(ct);

            return Ok(new
            {
                generatedAt = emptyAt,
                counts,
                summary = emptySummary
            });
        }

        var prompt = new StringBuilder();
        prompt.AppendLine($"Період: останні 24 години (з {since:yyyy-MM-dd HH:mm} UTC).");
        prompt.AppendLine($"Група: {group.Name}.");
        prompt.AppendLine();

        prompt.AppendLine($"Нові матеріали ({materials.Count}):");
        foreach (var m in materials)
            prompt.AppendLine($"- {m.FileName} (завантажив {m.Uploader}, {m.UploadedAt:HH:mm})");
        prompt.AppendLine();

        prompt.AppendLine($"Нові завдання ({assignments.Count}):");
        foreach (var a in assignments)
            prompt.AppendLine($"- {a.Title}: {a.Description} (дедлайн {a.DueDate:yyyy-MM-dd})");
        prompt.AppendLine();

        prompt.AppendLine($"Повідомлення в чаті ({messages.Count}):");
        foreach (var m in messages)
            prompt.AppendLine($"[{m.SentAt:HH:mm}] {m.Sender}: {m.Content}");

        // The AI call can fail when every model in the pool is rate-limited (429) or out of
        // credit. Surface a friendly 503 instead of leaking a raw 500 — the digest is an
        // auxiliary feature, a temporary AI outage must not look like a server crash.
        string rawSummary;
        try
        {
            rawSummary = await _ai.SummarizeAsync(SystemPrompt, prompt.ToString(), ct);
        }
        catch (InvalidOperationException ex)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                message = "AI-сервіс тимчасово недоступний. Спробуйте за хвилину.",
                detail = ex.Message
            });
        }
        var summary = SanitizeSummary(rawSummary);

        var generatedAt = DateTime.UtcNow;
        group.LastDigestSummary = summary;
        group.LastDigestGeneratedAt = generatedAt;
        group.LastDigestMaterialCount = counts.materials;
        group.LastDigestAssignmentCount = counts.assignments;
        group.LastDigestMessageCount = counts.messages;
        await _context.SaveChangesAsync(ct);


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
                "Новий дайджест за добу",
                "Староста згенерував підсумок активності групи.",
                ct: ct);
        }

        return Ok(new
        {
            generatedAt,
            counts,
            summary
        });
    }

    // Server-side safety net: strips markdown markers the LLM may still emit despite the prompt,
    // and normalises every list marker to «• » so the front-end can render plain whitespace-pre-wrap text.
    private static readonly Regex BoldMarkers = new(@"(\*\*|__)(.+?)\1", RegexOptions.Compiled);
    private static readonly Regex ItalicMarkers = new(@"(?<!\w)([*_])([^*_\n]+?)\1(?!\w)", RegexOptions.Compiled);
    private static readonly Regex LeadingHeading = new(@"^\s{0,3}#{1,6}\s+", RegexOptions.Compiled);
    private static readonly Regex LeadingBullet = new(@"^\s{0,3}([-*+])\s+", RegexOptions.Compiled);
    private static readonly Regex Backticks = new(@"`+", RegexOptions.Compiled);
    private static readonly Regex MultipleBlankLines = new(@"\n{3,}", RegexOptions.Compiled);

    public static string SanitizeSummary(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;

        var text = input.Replace("\r\n", "\n").Trim();
        text = BoldMarkers.Replace(text, "$2");
        text = ItalicMarkers.Replace(text, "$2");
        text = Backticks.Replace(text, string.Empty);

        var lines = text.Split('\n')
            .Select(line =>
            {
                var trimmed = LeadingHeading.Replace(line, string.Empty);
                trimmed = LeadingBullet.Replace(trimmed, "• ");
                return trimmed.TrimEnd();
            });

        var joined = string.Join('\n', lines);
        return MultipleBlankLines.Replace(joined, "\n\n").Trim();
    }
}
