using System.Security.Claims;
using HotChocolate.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using AkademVault_API.Data;
using AkademVault_API.GraphQL.Types;
using AkademVault_API.Models;

namespace AkademVault_API.GraphQL;

// Root GraphQL query — read-only surface co-existing with the REST API; covers query-heavy screens.
[Authorize]
public class Query
{
    // Browse all groups (optional name search). Mirrors GET /api/group, but with field selection.
    public async Task<List<GroupSummaryGql>> GetGroups(
        [Service] AppDbContext db,
        string? search,
        CancellationToken ct)
    {
        var query = db.Groups.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(g => EF.Functions.ILike(g.Name, $"%{search}%"));

        return await query
            .OrderBy(g => g.Name)
            .Select(g => new GroupSummaryGql(
                g.Id,
                g.Name,
                g.ShortCode,
                g.Owner!.Username,
                g.Members.Count,
                g.ExpiryDate))
            .ToListAsync(ct);
    }

    // Returns the caller's current group with the full member roster (one EF projection, no N+1).
    public async Task<GroupDetailsGql?> GetMyGroup(
        [Service] AppDbContext db,
        [Service] IHttpContextAccessor http,
        CancellationToken ct)
    {
        var userId = GetUserId(http);
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user?.GroupId == null) return null;

        return await db.Groups
            .AsNoTracking()
            .Where(g => g.Id == user.GroupId)
            .Select(g => new GroupDetailsGql(
                g.Id,
                g.Name,
                g.ShortCode,
                g.OwnerId,
                g.Owner!.Username,
                g.CreatedAt,
                g.ExpiryDate,
                g.Members.Select(m => new GroupMemberGql(m.Id, m.Username, m.Id == g.OwnerId)).ToList()))
            .FirstOrDefaultAsync(ct);
    }

    // Returns one material with its threaded comment tree built server-side.
    public async Task<MaterialWithCommentsGql?> GetMaterial(
        [Service] AppDbContext db,
        [Service] IHttpContextAccessor http,
        Guid id,
        CancellationToken ct)
    {
        var userId = GetUserId(http);
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user?.GroupId == null) return null;

        var material = await db.LectureMaterials
            .AsNoTracking()
            .Where(m => m.Id == id && m.GroupId == user.GroupId)
            .Select(m => new
            {
                m.Id,
                m.FileName,
                m.ContentType,
                m.SizeBytes,
                m.GroupId,
                m.UploaderId,
                UploaderName = m.Uploader!.Username,
                m.UploadedAt
            })
            .FirstOrDefaultAsync(ct);

        if (material == null) return null;

        var flat = await db.MaterialComments
            .AsNoTracking()
            .Where(c => c.MaterialId == id)
            .OrderBy(c => c.CreatedAt)
            .Select(c => new MaterialCommentGql(
                c.Id,
                c.ParentCommentId,
                c.AuthorId,
                c.Author!.Username,
                c.Content,
                c.CreatedAt,
                new List<MaterialCommentGql>()))
            .ToListAsync(ct);

        return new MaterialWithCommentsGql(
            material.Id,
            material.FileName,
            material.ContentType,
            material.SizeBytes,
            material.GroupId,
            material.UploaderId,
            material.UploaderName,
            material.UploadedAt,
            BuildTree(flat));
    }

    // Aggregates today's schedule + upcoming assignments + 5 most recent materials + aggregate counts in one round-trip.
    public async Task<DashboardGql?> GetDashboard(
        [Service] AppDbContext db,
        [Service] IHttpContextAccessor http,
        CancellationToken ct)
    {
        var userId = GetUserId(http);
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user?.GroupId == null) return null;

        var groupId = user.GroupId.Value;
        var today = DateTime.UtcNow.DayOfWeek;
        var yesterday = DateTime.UtcNow.AddDays(-1);

        var todaySchedule = await db.ScheduleEntries
            .AsNoTracking()
            .Where(s => s.GroupId == groupId && s.DayOfWeek == today)
            .OrderBy(s => s.StartTime)
            .Select(s => new DashboardScheduleEntryGql(
                s.Id,
                s.Title,
                s.Type.ToString(),
                s.DayOfWeek,
                s.StartTime,
                s.EndTime,
                s.Location,
                s.Teacher))
            .ToListAsync(ct);

        // Upcoming = assignments due from yesterday onwards (mirrors the original REST dashboard filter).
        var upcomingAssignments = await db.Assignments
            .AsNoTracking()
            .Where(a => a.GroupId == groupId && a.DueDate >= yesterday)
            .OrderBy(a => a.DueDate)
            .Select(a => new DashboardAssignmentGql(a.Id, a.Title, a.Description, a.DueDate))
            .ToListAsync(ct);

        var recentMaterials = await db.LectureMaterials
            .AsNoTracking()
            .Where(m => m.GroupId == groupId)
            .OrderByDescending(m => m.UploadedAt)
            .Take(5)
            .Select(m => new DashboardMaterialGql(
                m.Id,
                m.FileName,
                m.ContentType,
                m.SizeBytes,
                m.Uploader!.Username,
                m.UploadedAt))
            .ToListAsync(ct);

        var scheduleTotal = await db.ScheduleEntries.AsNoTracking().CountAsync(s => s.GroupId == groupId, ct);
        var materialTotal = await db.LectureMaterials.AsNoTracking().CountAsync(m => m.GroupId == groupId, ct);

        return new DashboardGql(todaySchedule, upcomingAssignments, recentMaterials, scheduleTotal, materialTotal);
    }

    // Rebuilds a flat comment list into a parent→replies tree in one O(n) pass.
    private static List<MaterialCommentGql> BuildTree(List<MaterialCommentGql> flat)
    {
        var byId = flat.ToDictionary(c => c.Id);
        var roots = new List<MaterialCommentGql>();
        foreach (var c in flat)
        {
            if (c.ParentCommentId.HasValue && byId.TryGetValue(c.ParentCommentId.Value, out var parent))
                parent.Replies.Add(c);
            else
                roots.Add(c);
        }
        return roots;
    }

    // Extracts the authenticated user id from the cookie-auth ClaimsPrincipal on the current request.
    private static Guid GetUserId(IHttpContextAccessor http)
    {
        var raw = http.HttpContext?.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? throw new GraphQLException("Not authenticated.");
        return Guid.Parse(raw);
    }
}
