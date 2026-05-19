using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AkademVault_API.Data;
using AkademVault_API.Models;
using AkademVault_API.Services;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;

namespace AkademVault_API.Controllers;

// Group lifecycle endpoints: create, browse, view-own, leave, and Owner-only kick.
[Authorize]
[ApiController]
[Route("api/[controller]")]
public class GroupController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly IShortCodeGenerator _codes;

    public GroupController(AppDbContext context, IShortCodeGenerator codes)
    {
        _context = context;
        _codes = codes;
    }

    // Creates a group with a unique short code, sets the caller as Owner, and joins them to it.
    [HttpPost("create")]
    public async Task<IActionResult> CreateGroup([FromBody] CreateGroupRequest request)
    {
        var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

        var user = await _context.Users.FindAsync(userId);
        if (user?.GroupId != null)
            return BadRequest(new { message = "Ви вже перебуваєте в групі" });

        string shortCode;
        do
        {
            shortCode = _codes.Generate();
        } while (await _context.Groups.AnyAsync(g => g.ShortCode == shortCode));

        var group = new Group
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            ShortCode = shortCode,
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


    // Lists all groups for the join-request UI; optional ILIKE search filter.
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
                g.ShortCode,
                g.Owner!.Username,
                g.Members.Count,
                g.ExpiryDate))
            .ToListAsync();

        return Ok(groups);
    }


    // Returns the caller's current group with the full member roster.
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
                g.ShortCode,
                g.OwnerId,
                g.Owner!.Username,
                g.CreatedAt,
                g.ExpiryDate,
                g.Members.Select(m => new GroupMemberDto(m.Id, m.Username, m.Id == g.OwnerId)).ToList()))
            .FirstOrDefaultAsync();

        return group == null ? NotFound(new { message = "Групу не знайдено." }) : Ok(group);
    }


    // Removes the caller from their group; Owner must transfer/delete instead.
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


    // Owner-only: removes a member from the Owner's group.
    [HttpPost("kick/{userId}")]
    public async Task<IActionResult> Kick(Guid userId)
    {
        var ownerId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        var group = await _context.Groups.FirstOrDefaultAsync(g => g.OwnerId == ownerId);

        if (group == null)
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "Тільки староста може виганяти учасників." });

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

// Request body for POST /group/create.
public class CreateGroupRequest
{
    [Required(ErrorMessage = "Назва групи обов'язкова")]
    [StringLength(50, MinimumLength = 2, ErrorMessage = "Назва має бути від 2 до 50 символів")]
    public string Name { get; set; } = string.Empty;

    [Range(1, 7, ErrorMessage = "Тривалість навчання має бути від 1 до 7 років")]
    public int YearsOfStudy { get; set; } = 4;
}

// Compact group projection for the browse list.
public record GroupSummaryDto(Guid Id, string Name, string ShortCode, string OwnerName, int MemberCount, DateTime ExpiryDate);

// Member row used inside GroupDetailsDto.
public record GroupMemberDto(Guid Id, string Username, bool IsOwner);

// Full group view for the Owner/member UI, including the member roster.
public record GroupDetailsDto(
    Guid Id,
    string Name,
    string ShortCode,
    Guid OwnerId,
    string OwnerName,
    DateTime CreatedAt,
    DateTime ExpiryDate,
    List<GroupMemberDto> Members);
