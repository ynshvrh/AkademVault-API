namespace AkademVault_API.Controllers;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using AkademVault_API.Data;
using AkademVault_API.Models;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class RequestController : ControllerBase
{
    private readonly AppDbContext _context;

    public RequestController(AppDbContext context) => _context = context;

    
    [HttpPost("send/{groupId}")]
    public async Task<IActionResult> SendRequest(Guid groupId)
    {
        var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

        
        var user = await _context.Users.FindAsync(userId);
        if (user?.GroupId != null) return BadRequest("Ви вже в групі");

     
        var existingRequest = await _context.JoinRequests
            .AnyAsync(r => r.GroupId == groupId && r.UserId == userId && r.Status == RequestStatus.Pending);
        
        if (existingRequest) return BadRequest("Заявка вже надіслана");

        var request = new JoinRequest { Id = Guid.NewGuid(), GroupId = groupId, UserId = userId };
        _context.JoinRequests.Add(request);
        await _context.SaveChangesAsync();

        return Ok("Заявку надіслано власнику групи");
    }

   
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

    
    [HttpPost("approve/{requestId}")]
    public async Task<IActionResult> Approve(Guid requestId)
    {
        var ownerId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        var request = await _context.JoinRequests.Include(r => r.Group).FirstOrDefaultAsync(r => r.Id == requestId);

        if (request == null || request.Group!.OwnerId != ownerId) return Forbid();

        request.Status = RequestStatus.Approved;
      
        var targetUser = await _context.Users.FindAsync(request.UserId);
        if (targetUser != null) targetUser.GroupId = request.GroupId;

        await _context.SaveChangesAsync();
        return Ok("Користувача додано до групи");
    }
   
[HttpPost("reject/{requestId}")]
public async Task<IActionResult> Reject(Guid requestId)
{
    var ownerId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
    
   
    var request = await _context.JoinRequests
        .Include(r => r.Group)
        .FirstOrDefaultAsync(r => r.Id == requestId);

    if (request == null || request.Group!.OwnerId != ownerId) 
        return Forbid(); 

    request.Status = RequestStatus.Rejected;
    await _context.SaveChangesAsync();

    return Ok("Заявку відхилено");
}
}