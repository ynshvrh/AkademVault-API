namespace AkademVault_API.Controllers;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using AkademVault_API.Data;
using AkademVault_API.Models;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;

// Self-service join-requests: a user asks to join a group; the Owner approves or rejects.
[Authorize]
[ApiController]
[Route("api/[controller]")]
public class RequestController : ControllerBase
{
    private readonly AppDbContext _context;

    public RequestController(AppDbContext context) => _context = context;


    // Creates a Pending join-request from the caller to the target group (one open request per user/group).
    [HttpPost("send/{groupId}")]
    public async Task<IActionResult> SendRequest(Guid groupId)
    {
        var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);


        var user = await _context.Users.FindAsync(userId);
        if (user?.GroupId != null) return BadRequest(new { message = "Ви вже в групі" });


        var existingRequest = await _context.JoinRequests
            .AnyAsync(r => r.GroupId == groupId && r.UserId == userId && r.Status == RequestStatus.Pending);

        if (existingRequest) return BadRequest(new { message = "Заявка вже надіслана" });

        var request = new JoinRequest { Id = Guid.NewGuid(), GroupId = groupId, UserId = userId };
        _context.JoinRequests.Add(request);
        await _context.SaveChangesAsync();

        return Ok(new { message = "Заявку надіслано власнику групи" });
    }


    // Owner-only: lists Pending join-requests targeting any group the caller owns.
    [HttpGet("pending")]
    public async Task<IActionResult> GetPendingRequests()
    {
        var ownerId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

        var requests = await _context.JoinRequests
            .Include(r => r.User)
            .Where(r => r.Group!.OwnerId == ownerId && r.Status == RequestStatus.Pending)
            .Select(r => new { r.Id, r.User!.Username, r.CreatedAt })
            .ToListAsync();

        return Ok(requests);
    }


    // Owner-only: approves a join-request and adds the user to the Owner's group.
    [HttpPost("approve/{requestId}")]
    public async Task<IActionResult> Approve(Guid requestId)
    {
        var ownerId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        var request = await _context.JoinRequests.Include(r => r.Group).FirstOrDefaultAsync(r => r.Id == requestId);

        if (request == null || request.Group!.OwnerId != ownerId) return StatusCode(StatusCodes.Status403Forbidden, new { message = "Тільки староста цієї групи може виконати дію." });

        request.Status = RequestStatus.Approved;

        var targetUser = await _context.Users.FindAsync(request.UserId);
        if (targetUser != null) targetUser.GroupId = request.GroupId;

        await _context.SaveChangesAsync();
        return Ok(new { message = "Користувача додано до групи" });
    }

    // Owner-only: rejects a join-request without modifying the user's group membership.
    [HttpPost("reject/{requestId}")]
    public async Task<IActionResult> Reject(Guid requestId)
    {
        var ownerId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);


        var request = await _context.JoinRequests
            .Include(r => r.Group)
            .FirstOrDefaultAsync(r => r.Id == requestId);

        if (request == null || request.Group!.OwnerId != ownerId)
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "Тільки староста цієї групи може виконати дію." });

        request.Status = RequestStatus.Rejected;
        await _context.SaveChangesAsync();

        return Ok(new { message = "Заявку відхилено" });
    }
}
