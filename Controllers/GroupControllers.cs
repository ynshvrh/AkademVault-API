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
public class GroupController : ControllerBase
{
    private readonly AppDbContext _context;

    public GroupController(AppDbContext context)
    {
        _context = context;
    }

    [HttpPost("create")]
    public async Task<IActionResult> CreateGroup([FromBody] CreateGroupRequest request)
    {
        var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

        var user = await _context.Users.FindAsync(userId);
        if (user?.GroupId != null)
            return BadRequest(new { message = "Ви вже перебуваєте в групі" });

        var group = new Group
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            CreatedAt = DateTime.UtcNow,
            ExpiryDate = DateTime.UtcNow.AddYears(request.YearsOfStudy),
            OwnerId = userId
        };

        _context.Groups.Add(group);
        user!.GroupId = group.Id;

        await _context.SaveChangesAsync();

        return Ok(new {
            message = "Групу створено",
            groupName = group.Name,
            expiresAt = group.ExpiryDate
        });
    }


    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] string? search = null)
    {
        var query = _context.Groups.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(g => EF.Functions.ILike(g.Name, $"%{search}%"));

        var groups = await query
            .OrderBy(g => g.Name)
            .Select(g => new GroupSummaryDto(
                g.Id,
                g.Name,
                g.Owner!.Username,
                g.Members.Count,
                g.ExpiryDate))
            .ToListAsync();

        return Ok(groups);
    }


    [HttpGet("mine")]
    public async Task<IActionResult> GetMine()
    {
        var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

        var user = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
        if (user?.GroupId == null)
            return NotFound(new { message = "Ви не належите до жодної групи" });

        var group = await _context.Groups
            .AsNoTracking()
            .Where(g => g.Id == user.GroupId)
            .Select(g => new GroupDetailsDto(
                g.Id,
                g.Name,
                g.OwnerId,
                g.Owner!.Username,
                g.CreatedAt,
                g.ExpiryDate,
                g.Members.Select(m => new GroupMemberDto(m.Id, m.Username, m.Id == g.OwnerId)).ToList()))
            .FirstOrDefaultAsync();

        return group == null ? NotFound() : Ok(group);
    }


    [HttpPost("leave")]
    public async Task<IActionResult> Leave()
    {
        var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        var user = await _context.Users.FindAsync(userId);

        if (user?.GroupId == null)
            return BadRequest(new { message = "Ви не в групі" });

        var group = await _context.Groups.FindAsync(user.GroupId);
        if (group?.OwnerId == userId)
            return BadRequest(new { message = "Староста не може вийти з групи. Передайте права або видаліть групу." });

        user.GroupId = null;
        await _context.SaveChangesAsync();

        return Ok(new { message = "Ви вийшли з групи" });
    }


    [HttpPost("kick/{userId}")]
    public async Task<IActionResult> Kick(Guid userId)
    {
        var ownerId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        var group = await _context.Groups.FirstOrDefaultAsync(g => g.OwnerId == ownerId);

        if (group == null)
            return Forbid();

        if (userId == ownerId)
            return BadRequest(new { message = "Не можна вигнати самого себе" });

        var target = await _context.Users.FindAsync(userId);
        if (target == null || target.GroupId != group.Id)
            return NotFound(new { message = "Користувача немає у вашій групі" });

        target.GroupId = null;
        await _context.SaveChangesAsync();

        return Ok(new { message = $"{target.Username} вигнаний з групи" });
    }
}

public class CreateGroupRequest
{
    public string Name { get; set; } = string.Empty;
    public int YearsOfStudy { get; set; } = 4;
}

public record GroupSummaryDto(Guid Id, string Name, string OwnerName, int MemberCount, DateTime ExpiryDate);

public record GroupMemberDto(Guid Id, string Username, bool IsOwner);

public record GroupDetailsDto(
    Guid Id,
    string Name,
    Guid OwnerId,
    string OwnerName,
    DateTime CreatedAt,
    DateTime ExpiryDate,
    List<GroupMemberDto> Members);
