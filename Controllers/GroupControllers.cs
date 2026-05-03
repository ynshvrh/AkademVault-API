using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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
}

public class CreateGroupRequest
{
    public string Name { get; set; } = string.Empty;
    public int YearsOfStudy { get; set; } = 4;
}