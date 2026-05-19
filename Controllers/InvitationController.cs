using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AkademVault_API.Data;
using AkademVault_API.Models;
using AkademVault_API.Services;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Security.Cryptography;

namespace AkademVault_API.Controllers;

// Personal invitations and short-lived shareable invite links for a group.
[Authorize]
[ApiController]
[Route("api/[controller]")]
public class InvitationController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly INotificationService _notifications;

    private static readonly TimeSpan LinkTtl = TimeSpan.FromDays(30);

    public InvitationController(AppDbContext context, INotificationService notifications)
    {
        _context = context;
        _notifications = notifications;
    }


    // Owner-only: sends a personal invitation to a user by username or email and fans out a notification.
    [HttpPost("send")]
    public async Task<IActionResult> Send([FromBody] SendInvitationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.UsernameOrEmail))
            return BadRequest(new { message = "Вкажіть username або email" });

        var ownerId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        var group = await _context.Groups.FirstOrDefaultAsync(g => g.OwnerId == ownerId);
        if (group == null)
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "Тільки староста може запрошувати." });

        var query = request.UsernameOrEmail.Trim();
        var target = await _context.Users.FirstOrDefaultAsync(u =>
            u.Username == query || u.Email == query);

        if (target == null) return NotFound(new { message = "Користувача не знайдено" });
        if (target.Id == ownerId) return BadRequest(new { message = "Не можна запрошувати самого себе" });
        if (target.GroupId == group.Id) return BadRequest(new { message = "Користувач вже у вашій групі" });

        var existing = await _context.Invitations.AnyAsync(i =>
            i.GroupId == group.Id && i.InvitedUserId == target.Id && i.Status == InvitationStatus.Pending);
        if (existing) return BadRequest(new { message = "Запрошення вже надіслано" });

        var invitation = new Invitation
        {
            Id = Guid.NewGuid(),
            GroupId = group.Id,
            InvitedUserId = target.Id,
            InvitedByUserId = ownerId,
            Status = InvitationStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };

        _context.Invitations.Add(invitation);
        await _context.SaveChangesAsync();

        await _notifications.NotifyAsync(
            target.Id,
            NotificationType.GroupInvitation,
            $"Вас запросили до групи {group.Name}",
            $"Староста групи {group.Name} ({group.ShortCode}) запрошує вас приєднатися.",
            invitation.Id);

        return Ok(new { invitationId = invitation.Id });
    }


    // Returns the caller's pending invitations to render in the invitations inbox.
    [HttpGet("inbox")]
    public async Task<IActionResult> Inbox()
    {
        var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

        var pending = await _context.Invitations
            .AsNoTracking()
            .Where(i => i.InvitedUserId == userId && i.Status == InvitationStatus.Pending)
            .OrderByDescending(i => i.CreatedAt)
            .Select(i => new InvitationDto(
                i.Id,
                i.GroupId,
                i.Group!.Name,
                i.Group.ShortCode,
                i.InvitedBy!.Username,
                i.CreatedAt))
            .ToListAsync();

        return Ok(pending);
    }


    // Accepts an invitation and joins the group; rejected if the user is already in another group.
    [HttpPost("{id}/accept")]
    public async Task<IActionResult> Accept(Guid id)
    {
        var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        var invitation = await _context.Invitations.FirstOrDefaultAsync(i => i.Id == id && i.InvitedUserId == userId);
        if (invitation == null) return NotFound(new { message = "Запрошення не знайдено." });
        if (invitation.Status != InvitationStatus.Pending) return BadRequest(new { message = "Це запрошення вже оброблено" });

        var user = await _context.Users.FindAsync(userId);
        if (user!.GroupId != null) return BadRequest(new { message = "Ви вже у групі. Вийдіть з поточної перш ніж приймати інше запрошення." });

        user.GroupId = invitation.GroupId;
        invitation.Status = InvitationStatus.Accepted;
        invitation.RespondedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return Ok(new { message = "Ви приєдналися до групи", groupId = invitation.GroupId });
    }


    // Declines a pending invitation without joining.
    [HttpPost("{id}/decline")]
    public async Task<IActionResult> Decline(Guid id)
    {
        var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        var invitation = await _context.Invitations.FirstOrDefaultAsync(i => i.Id == id && i.InvitedUserId == userId);
        if (invitation == null) return NotFound(new { message = "Запрошення не знайдено." });
        if (invitation.Status != InvitationStatus.Pending) return BadRequest(new { message = "Це запрошення вже оброблено" });

        invitation.Status = InvitationStatus.Declined;
        invitation.RespondedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return Ok(new { message = "Запрошення відхилено" });
    }


    // Owner-only: mints a shareable invite link valid for the configured TTL.
    [HttpPost("links")]
    public async Task<IActionResult> CreateLink()
    {
        var ownerId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        var group = await _context.Groups.FirstOrDefaultAsync(g => g.OwnerId == ownerId);
        if (group == null)
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "Тільки староста може створювати лінк." });

        var token = GenerateToken();
        var link = new GroupInviteLink
        {
            Id = Guid.NewGuid(),
            GroupId = group.Id,
            CreatedByUserId = ownerId,
            Token = token,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.Add(LinkTtl)
        };

        _context.GroupInviteLinks.Add(link);
        await _context.SaveChangesAsync();

        return Ok(new InviteLinkDto(link.Id, link.Token, link.CreatedAt, link.ExpiresAt, null));
    }


    // Owner-only: lists all invite links of the Owner's group (active + revoked + expired).
    [HttpGet("links")]
    public async Task<IActionResult> ListLinks()
    {
        var ownerId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        var group = await _context.Groups.AsNoTracking().FirstOrDefaultAsync(g => g.OwnerId == ownerId);
        if (group == null)
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "Тільки староста може переглядати лінки." });

        var links = await _context.GroupInviteLinks
            .AsNoTracking()
            .Where(l => l.GroupId == group.Id)
            .OrderByDescending(l => l.CreatedAt)
            .Select(l => new InviteLinkDto(l.Id, l.Token, l.CreatedAt, l.ExpiresAt, l.RevokedAt))
            .ToListAsync();

        return Ok(links);
    }


    // Owner-only: marks an invite link as revoked so further accepts are refused.
    [HttpPost("links/{id}/revoke")]
    public async Task<IActionResult> RevokeLink(Guid id)
    {
        var ownerId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        var link = await _context.GroupInviteLinks.Include(l => l.Group).FirstOrDefaultAsync(l => l.Id == id);
        if (link == null) return NotFound(new { message = "Лінк не знайдено." });
        if (link.Group?.OwnerId != ownerId)
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "Лінк належить іншій групі." });
        if (link.RevokedAt != null) return BadRequest(new { message = "Лінк вже відкликаний" });

        link.RevokedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return Ok();
    }


    // Owner-only: hard-deletes every revoked OR expired link of the Owner's group.
    // Active (non-revoked, not yet expired) links are preserved so existing shares keep working.
    [HttpDelete("links/cleanup")]
    public async Task<IActionResult> CleanupLinks()
    {
        var ownerId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        var group = await _context.Groups.AsNoTracking().FirstOrDefaultAsync(g => g.OwnerId == ownerId);
        if (group == null)
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "Тільки староста може чистити лінки." });

        var now = DateTime.UtcNow;
        var dead = await _context.GroupInviteLinks
            .Where(l => l.GroupId == group.Id && (l.RevokedAt != null || l.ExpiresAt < now))
            .ToListAsync();

        if (dead.Count == 0) return Ok(new { removed = 0 });

        _context.GroupInviteLinks.RemoveRange(dead);
        await _context.SaveChangesAsync();
        return Ok(new { removed = dead.Count });
    }


    // Anonymous preview of an invite link so guests can see the group before signing in.
    [HttpGet("links/by-token/{token}/preview")]
    [AllowAnonymous]
    public async Task<IActionResult> PreviewLink(string token)
    {
        var link = await _context.GroupInviteLinks
            .AsNoTracking()
            .Include(l => l.Group)
            .FirstOrDefaultAsync(l => l.Token == token);

        if (link == null) return NotFound(new { message = "Лінк не знайдено." });
        if (link.RevokedAt != null) return BadRequest(new { message = "Лінк відкликаний" });
        if (link.ExpiresAt < DateTime.UtcNow) return BadRequest(new { message = "Термін дії лінку минув" });

        return Ok(new
        {
            groupId = link.GroupId,
            groupName = link.Group!.Name,
            groupShortCode = link.Group.ShortCode,
            expiresAt = link.ExpiresAt
        });
    }


    // Joins the caller to the group referenced by the link if it is still valid.
    [HttpPost("links/by-token/{token}/accept")]
    public async Task<IActionResult> AcceptLink(string token)
    {
        var link = await _context.GroupInviteLinks.FirstOrDefaultAsync(l => l.Token == token);
        if (link == null) return NotFound(new { message = "Лінк не знайдено." });
        if (link.RevokedAt != null) return BadRequest(new { message = "Лінк відкликаний" });
        if (link.ExpiresAt < DateTime.UtcNow) return BadRequest(new { message = "Термін дії лінку минув" });

        var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        var user = await _context.Users.FindAsync(userId);
        if (user!.GroupId == link.GroupId) return BadRequest(new { message = "Ви вже в цій групі" });
        if (user.GroupId != null) return BadRequest(new { message = "Ви вже в іншій групі. Вийдіть з поточної перш ніж приєднуватися." });

        user.GroupId = link.GroupId;
        await _context.SaveChangesAsync();

        return Ok(new { message = "Ви приєдналися до групи", groupId = link.GroupId });
    }


    // URL-safe base64 of 24 random bytes — used as the opaque invite-link token.
    private static string GenerateToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(24);
        return Convert.ToBase64String(bytes).Replace("+", "-").Replace("/", "_").TrimEnd('=');
    }
}

// Request body for POST /invitation/send.
public class SendInvitationRequest
{
    [Required(ErrorMessage = "Username або email обов'язкові")]
    [StringLength(100, MinimumLength = 1)]
    public string UsernameOrEmail { get; set; } = string.Empty;
}

// Pending-invitation row shown in the inbox.
public record InvitationDto(
    Guid Id,
    Guid GroupId,
    string GroupName,
    string GroupShortCode,
    string InvitedByName,
    DateTime CreatedAt);

// Owner-side projection of a shareable invite link.
public record InviteLinkDto(
    Guid Id,
    string Token,
    DateTime CreatedAt,
    DateTime ExpiresAt,
    DateTime? RevokedAt);
