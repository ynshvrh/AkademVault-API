using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AkademVault_API.Data;
using AkademVault_API.Models;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;

namespace AkademVault_API.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class PlannerController : ControllerBase
{
    private readonly AppDbContext _context;

    public PlannerController(AppDbContext context) => _context = context;


    [HttpGet("assignments")]
    public async Task<IActionResult> GetAssignments()
    {
        var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        var user = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);

        if (user?.GroupId == null)
            return BadRequest(new { message = "Ви не належите до жодної групи." });

        var assignments = await _context.Assignments
            .AsNoTracking()
            .Where(a => a.GroupId == user.GroupId)
            .OrderBy(a => a.DueDate)
            .Select(a => new AssignmentResponseDto(a.Id, a.Title, a.Description, a.DueDate, a.GroupId, a.CreatedAt))
            .ToListAsync();

        return Ok(assignments);
    }


    [HttpPost("assignments")]
    public async Task<IActionResult> CreateAssignment([FromBody] AssignmentDto dto)
    {
        var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        var group = await _context.Groups.FirstOrDefaultAsync(g => g.OwnerId == userId);

        if (group == null)
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "Тільки староста може створювати завдання." });

        var assignment = new Assignment
        {
            Id = Guid.NewGuid(),
            Title = dto.Title,
            Description = dto.Description,
            DueDate = dto.DueDate,
            GroupId = group.Id,
            CreatedAt = DateTime.UtcNow
        };

        _context.Assignments.Add(assignment);
        await _context.SaveChangesAsync();

        return Ok(new AssignmentResponseDto(assignment.Id, assignment.Title, assignment.Description,
            assignment.DueDate, assignment.GroupId, assignment.CreatedAt));
    }


    [HttpPut("assignments/{id}")]
    public async Task<IActionResult> UpdateAssignment(Guid id, [FromBody] AssignmentDto dto)
    {
        var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        var assignment = await _context.Assignments.Include(a => a.Group).FirstOrDefaultAsync(a => a.Id == id);

        if (assignment == null) return NotFound(new { message = "Завдання не знайдено." });
        if (assignment.Group?.OwnerId != userId)
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "Редагувати може тільки староста групи." });

        assignment.Title = dto.Title;
        assignment.Description = dto.Description;
        assignment.DueDate = dto.DueDate;

        await _context.SaveChangesAsync();

        return Ok(new AssignmentResponseDto(assignment.Id, assignment.Title, assignment.Description,
            assignment.DueDate, assignment.GroupId, assignment.CreatedAt));
    }


    [HttpDelete("assignments/{id}")]
    public async Task<IActionResult> DeleteAssignment(Guid id)
    {
        var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        var assignment = await _context.Assignments.Include(a => a.Group).FirstOrDefaultAsync(a => a.Id == id);

        if (assignment == null) return NotFound(new { message = "Завдання не знайдено." });
        if (assignment.Group?.OwnerId != userId)
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "Видалити може тільки староста групи." });

        _context.Assignments.Remove(assignment);
        await _context.SaveChangesAsync();
        return Ok(new { message = "Завдання видалено" });
    }


    [HttpGet("week")]
    public async Task<IActionResult> GetWeeklyAssignments()
    {
        var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        var user = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);

        if (user?.GroupId == null) return BadRequest(new { message = "Ви не належите до жодної групи." });

        DateTime now = DateTime.UtcNow;
        int diff = (7 + (now.DayOfWeek - DayOfWeek.Monday)) % 7;
        DateTime startOfWeek = now.AddDays(-1 * diff).Date;
        DateTime endOfWeek = startOfWeek.AddDays(7);

        var assignments = await _context.Assignments
            .AsNoTracking()
            .Where(a => a.GroupId == user.GroupId && a.DueDate >= startOfWeek && a.DueDate < endOfWeek)
            .OrderBy(a => a.DueDate)
            .Select(a => new AssignmentResponseDto(a.Id, a.Title, a.Description, a.DueDate, a.GroupId, a.CreatedAt))
            .ToListAsync();

        return Ok(assignments);
    }
}

public record AssignmentDto(
    [Required(ErrorMessage = "Назва обов'язкова")]
    [StringLength(100, MinimumLength = 1, ErrorMessage = "Назва має бути від 1 до 100 символів")]
    string Title,
    [StringLength(2000, ErrorMessage = "Опис не може бути довшим за 2000 символів")]
    string Description,
    [Required(ErrorMessage = "Дедлайн обов'язковий")]
    DateTime DueDate);

public record AssignmentResponseDto(
    Guid Id,
    string Title,
    string Description,
    DateTime DueDate,
    Guid GroupId,
    DateTime CreatedAt);
