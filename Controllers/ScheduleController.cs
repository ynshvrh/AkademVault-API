using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AkademVault_API.Data;
using AkademVault_API.Models;
using AkademVault_API.Services;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;

namespace AkademVault_API.Controllers;

// Weekly schedule CRUD plus AI-driven import from PDF/XLSX/image files.
[Authorize]
[ApiController]
[Route("api/[controller]")]
public class ScheduleController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly IScheduleParser _parser;

    private static readonly long MaxFileSize = 10 * 1024 * 1024;

    private static readonly HashSet<string> AllowedParseMimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/pdf",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        "image/png",
        "image/jpeg",
        "image/webp"
    };

    public ScheduleController(AppDbContext context, IScheduleParser parser)
    {
        _context = context;
        _parser = parser;
    }


    // Returns the caller's group schedule ordered by weekday/time for the calendar view.
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        var user = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
        if (user?.GroupId == null) return BadRequest(new { message = "Ви не належите до жодної групи." });

        var entries = await _context.ScheduleEntries
            .AsNoTracking()
            .Where(s => s.GroupId == user.GroupId)
            .OrderBy(s => s.DayOfWeek).ThenBy(s => s.StartTime)
            .Select(s => new ScheduleEntryDto(
                s.Id,
                s.Title,
                s.Type.ToString(),
                s.DayOfWeek,
                s.StartTime,
                s.EndTime,
                s.Location,
                s.Teacher))
            .ToListAsync();

        return Ok(entries);
    }


    // Owner-only: creates a single schedule entry after validating the time window.
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] ScheduleEntryWriteDto dto)
    {
        var (group, error) = await GetOwnedGroupAsync();
        if (error != null) return error;

        var entry = new ScheduleEntry
        {
            Id = Guid.NewGuid(),
            GroupId = group!.Id,
            Title = dto.Title,
            Type = dto.Type,
            DayOfWeek = dto.DayOfWeek,
            StartTime = dto.StartTime,
            EndTime = dto.EndTime,
            Location = dto.Location,
            Teacher = dto.Teacher,
            CreatedAt = DateTime.UtcNow
        };

        if (entry.EndTime <= entry.StartTime)
            return BadRequest(new { message = "Час кінця має бути пізніше за час початку." });

        _context.ScheduleEntries.Add(entry);
        await _context.SaveChangesAsync();
        return Ok(ToDto(entry));
    }


    // Owner-only: replaces an entry's fields if it belongs to the Owner's group.
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] ScheduleEntryWriteDto dto)
    {
        var (group, error) = await GetOwnedGroupAsync();
        if (error != null) return error;

        var entry = await _context.ScheduleEntries.FirstOrDefaultAsync(s => s.Id == id);
        if (entry == null) return NotFound(new { message = "Подію не знайдено." });
        if (entry.GroupId != group!.Id)
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "Подія належить іншій групі." });
        if (dto.EndTime <= dto.StartTime)
            return BadRequest(new { message = "Час кінця має бути пізніше за час початку." });

        entry.Title = dto.Title;
        entry.Type = dto.Type;
        entry.DayOfWeek = dto.DayOfWeek;
        entry.StartTime = dto.StartTime;
        entry.EndTime = dto.EndTime;
        entry.Location = dto.Location;
        entry.Teacher = dto.Teacher;

        await _context.SaveChangesAsync();
        return Ok(ToDto(entry));
    }

    private static ScheduleEntryDto ToDto(ScheduleEntry e) => new(
        e.Id, e.Title, e.Type.ToString(), e.DayOfWeek,
        e.StartTime, e.EndTime, e.Location, e.Teacher);


    // Owner-only: deletes a single entry by id.
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var (group, error) = await GetOwnedGroupAsync();
        if (error != null) return error;

        var entry = await _context.ScheduleEntries.FirstOrDefaultAsync(s => s.Id == id);
        if (entry == null) return NotFound(new { message = "Подію не знайдено." });
        if (entry.GroupId != group!.Id)
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "Подія належить іншій групі." });

        _context.ScheduleEntries.Remove(entry);
        await _context.SaveChangesAsync();
        return Ok(new { message = "Подію видалено" });
    }


    // Owner-only: wipes the entire group schedule (used before reimporting from a file).
    [HttpDelete("all")]
    public async Task<IActionResult> DeleteAll()
    {
        var (group, error) = await GetOwnedGroupAsync();
        if (error != null) return error;

        var deleted = await _context.ScheduleEntries
            .Where(s => s.GroupId == group!.Id)
            .ExecuteDeleteAsync();

        return Ok(new { deleted });
    }


    // Owner-only: hands an uploaded PDF/XLSX/image to the AI parser and returns proposed entries (not yet saved).
    [HttpPost("parse")]
    [RequestSizeLimit(10_485_760)]
    public async Task<IActionResult> Parse(IFormFile file, CancellationToken ct)
    {
        var (group, error) = await GetOwnedGroupAsync();
        if (error != null) return error;

        if (file == null || file.Length == 0)
            return BadRequest(new { message = "Файл не надано." });
        if (file.Length > MaxFileSize)
            return BadRequest(new { message = "Файл перевищує ліміт 10 MB." });
        if (!AllowedParseMimeTypes.Contains(file.ContentType))
            return BadRequest(new { message = "Дозволені формати: .pdf, .xlsx, .png, .jpg, .webp" });

        byte[] data;
        await using (var ms = new MemoryStream())
        {
            await file.CopyToAsync(ms, ct);
            data = ms.ToArray();
        }

        try
        {
            var entries = await _parser.ParseAsync(file.FileName, file.ContentType, data, ct);
            return Ok(entries);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = "Не вдалося розпарсити файл", detail = ex.Message });
        }
    }


    // Owner-only: persists a batch of entries (typically the user-reviewed AI parse result).
    [HttpPost("confirm")]
    public async Task<IActionResult> Confirm([FromBody] List<ScheduleEntryWriteDto> entries)
    {
        var (group, error) = await GetOwnedGroupAsync();
        if (error != null) return error;
        if (entries == null || entries.Count == 0)
            return BadRequest(new { message = "Список подій порожній." });

        var saved = new List<ScheduleEntry>();
        foreach (var dto in entries)
        {
            if (dto.EndTime <= dto.StartTime) continue;
            saved.Add(new ScheduleEntry
            {
                Id = Guid.NewGuid(),
                GroupId = group!.Id,
                Title = dto.Title,
                Type = dto.Type,
                DayOfWeek = dto.DayOfWeek,
                StartTime = dto.StartTime,
                EndTime = dto.EndTime,
                Location = dto.Location,
                Teacher = dto.Teacher,
                CreatedAt = DateTime.UtcNow
            });
        }

        if (saved.Count == 0)
            return BadRequest(new { message = "Жодна подія не має валідних часів." });

        _context.ScheduleEntries.AddRange(saved);
        await _context.SaveChangesAsync();
        return Ok(new { saved = saved.Count });
    }


    // Looks up the group owned by the caller; returns a 403 IActionResult when the caller is not an Owner.
    private async Task<(Group? group, IActionResult? error)> GetOwnedGroupAsync()
    {
        var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        var group = await _context.Groups.FirstOrDefaultAsync(g => g.OwnerId == userId);
        if (group == null)
            return (null, StatusCode(StatusCodes.Status403Forbidden, new { message = "Тільки староста може керувати розкладом." }));
        return (group, null);
    }
}

// Read DTO for schedule entries returned to the SPA.
public record ScheduleEntryDto(
    Guid Id,
    string Title,
    string Type,
    DayOfWeek DayOfWeek,
    TimeOnly StartTime,
    TimeOnly EndTime,
    string? Location,
    string? Teacher);

// Write DTO for create/update/confirm endpoints.
public record ScheduleEntryWriteDto(
    [Required(ErrorMessage = "Назва обов'язкова")]
    [StringLength(150, MinimumLength = 1, ErrorMessage = "Назва має бути від 1 до 150 символів")]
    string Title,
    [Required] ScheduleEntryType Type,
    [Required] DayOfWeek DayOfWeek,
    [Required] TimeOnly StartTime,
    [Required] TimeOnly EndTime,
    [StringLength(100, ErrorMessage = "Локація не може бути довшою за 100 символів")]
    string? Location,
    [StringLength(100, ErrorMessage = "Викладач не може бути довшим за 100 символів")]
    string? Teacher);
