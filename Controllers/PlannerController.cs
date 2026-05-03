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
            return BadRequest("Ви не належите до жодної групи.");

        var assignments = await _context.Assignments
            .Where(a => a.GroupId == user.GroupId)
            .OrderBy(a => a.DueDate)
            .ToListAsync();

        return Ok(assignments);
    }

  
    [HttpPost("assignments")]
    public async Task<IActionResult> CreateAssignment([FromBody] AssignmentDto dto)
    {
        var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        var group = await _context.Groups.FirstOrDefaultAsync(g => g.OwnerId == userId);

        if (group == null)
            return Forbid("Тільки староста може створювати завдання.");

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
        return Ok(assignment);
    }

  
    [HttpPut("assignments/{id}")]
    public async Task<IActionResult> UpdateAssignment(Guid id, [FromBody] AssignmentDto dto)
    {
        var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        var assignment = await _context.Assignments.Include(a => a.Group).FirstOrDefaultAsync(a => a.Id == id);

        if (assignment == null) return NotFound();
        if (assignment.Group?.OwnerId != userId) return Forbid();

        assignment.Title = dto.Title;
        assignment.Description = dto.Description;
        assignment.DueDate = dto.DueDate;

        await _context.SaveChangesAsync();
        return Ok(assignment);
    }

  
    [HttpDelete("assignments/{id}")]
    public async Task<IActionResult> DeleteAssignment(Guid id)
    {
        var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        var assignment = await _context.Assignments.Include(a => a.Group).FirstOrDefaultAsync(a => a.Id == id);

        if (assignment == null) return NotFound();
        if (assignment.Group?.OwnerId != userId) return Forbid();

        _context.Assignments.Remove(assignment);
        await _context.SaveChangesAsync();
        return Ok("Завдання видалено");
    }

    [HttpGet("week")]
public async Task<IActionResult> GetWeeklyAssignments()
{
    var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
    var user = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
    
    if (user?.GroupId == null) return BadRequest("Група не знайдена.");

  
    DateTime now = DateTime.UtcNow;
    int diff = (7 + (now.DayOfWeek - DayOfWeek.Monday)) % 7;
    DateTime startOfWeek = now.AddDays(-1 * diff).Date;
    DateTime endOfWeek = startOfWeek.AddDays(7);

    var assignments = await _context.Assignments
        .Where(a => a.GroupId == user.GroupId && a.DueDate >= startOfWeek && a.DueDate < endOfWeek)
        .OrderBy(a => a.DueDate)
        .ToListAsync();

    return Ok(assignments);
}
}

public record AssignmentDto(string Title, string Description, DateTime DueDate);