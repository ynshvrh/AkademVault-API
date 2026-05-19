using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AkademVault_API.Data;
using AkademVault_API.Models;
using AkademVault_API.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Path = System.IO.Path;

namespace AkademVault_API.Controllers;

// Lecture-material storage: upload/list/download/delete files in R2 and threaded comments per material.
[Authorize]
[ApiController]
[Route("api/[controller]")]
public class StorageController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly IR2StorageService _storage;
    private readonly INotificationService _notifications;

    private static readonly long MaxFileSize = 25 * 1024 * 1024;

    // Non-image formats accepted as-is (extension → expected MIME).
    private static readonly Dictionary<string, string> AllowedDocumentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"]  = "application/pdf",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
    };

    // Image MIME types we accept on input; ALL of these are converted to webp before being stored on R2.
    private static readonly HashSet<string> AcceptedImageMimes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg", "image/jpg", "image/png", "image/webp", "image/gif", "image/bmp", "image/tiff"
    };

    private const string WebpMime = "image/webp";
    private const int WebpQuality = 80;

    public StorageController(AppDbContext context, IR2StorageService storage, INotificationService notifications)
    {
        _context = context;
        _storage = storage;
        _notifications = notifications;
    }


    // Uploads a file to R2 under groups/{groupId}/{materialId}{ext} and notifies the rest of the group.
    // Documents (.pdf/.docx) are stored as-is; images of any accepted format are transcoded to webp first.
    [HttpPost("upload")]
    [RequestSizeLimit(26_214_400)]
    public async Task<IActionResult> Upload(IFormFile file, CancellationToken ct = default)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { message = "Файл не надано." });

        if (file.Length > MaxFileSize)
            return BadRequest(new { message = "Файл перевищує ліміт 25 MB." });

        var originalExtension = Path.GetExtension(file.FileName).ToLowerInvariant();
        var originalContentType = file.ContentType ?? string.Empty;
        var isImage = originalContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);

        string storedExtension;
        string storedContentType;
        string storedFileName;
        Stream uploadStream;
        long storedSize;
        var disposables = new List<IDisposable>();

        try
        {
            if (isImage)
            {
                if (!AcceptedImageMimes.Contains(originalContentType))
                    return BadRequest(new { message = "Непідтримуваний формат зображення." });

                // Transcode every image (including already-webp uploads, to normalise quality/size).
                var webpBuffer = new MemoryStream();
                disposables.Add(webpBuffer);
                await using (var src = file.OpenReadStream())
                {
                    using var image = await Image.LoadAsync(src, ct);
                    var encoder = new WebpEncoder { Quality = WebpQuality };
                    await image.SaveAsync(webpBuffer, encoder, ct);
                }
                webpBuffer.Position = 0;
                uploadStream = webpBuffer;
                storedSize = webpBuffer.Length;
                storedExtension = ".webp";
                storedContentType = WebpMime;
                storedFileName = Path.ChangeExtension(Path.GetFileName(file.FileName), ".webp");
            }
            else
            {
                if (!AllowedDocumentTypes.TryGetValue(originalExtension, out var expectedContentType))
                    return BadRequest(new { message = "Дозволені формати: будь-яке зображення, .pdf, .docx" });

                if (!string.Equals(originalContentType, expectedContentType, StringComparison.OrdinalIgnoreCase))
                    return BadRequest(new { message = "Тип файлу не відповідає його розширенню." });

                var docStream = file.OpenReadStream();
                disposables.Add(docStream);
                uploadStream = docStream;
                storedSize = file.Length;
                storedExtension = originalExtension;
                storedContentType = expectedContentType;
                storedFileName = Path.GetFileName(file.FileName);
            }

            var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
            var user = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);

            if (user?.GroupId == null)
                return BadRequest(new { message = "Ви не належите до жодної групи." });

            var materialId = Guid.NewGuid();
            var key = $"groups/{user.GroupId}/{materialId}{storedExtension}";

            await _storage.UploadAsync(key, uploadStream, storedContentType);

            var material = new LectureMaterial
            {
                Id = materialId,
                GroupId = user.GroupId.Value,
                UploaderId = userId,
                FileName = storedFileName,
                ContentType = storedContentType,
                SizeBytes = storedSize,
                R2Key = key,
                UploadedAt = DateTime.UtcNow
            };

            _context.LectureMaterials.Add(material);
            await _context.SaveChangesAsync(ct);

            var recipients = await _context.Users
                .AsNoTracking()
                .Where(u => u.GroupId == user.GroupId && u.Id != userId)
                .Select(u => u.Id)
                .ToListAsync(ct);

            if (recipients.Count > 0)
            {
                await _notifications.NotifyManyAsync(
                    recipients,
                    NotificationType.MaterialUploaded,
                    $"{user.Username} завантажив новий матеріал",
                    material.FileName,
                    material.Id,
                    ct);
            }

            return Ok(new LectureMaterialDto(
                material.Id,
                material.FileName,
                material.ContentType,
                material.SizeBytes,
                userId,
                user.Username ?? string.Empty,
                material.UploadedAt));
        }
        catch (UnknownImageFormatException)
        {
            return BadRequest(new { message = "Файл не розпізнано як зображення." });
        }
        catch (InvalidImageContentException)
        {
            return BadRequest(new { message = "Зображення пошкоджене або має невідомий формат." });
        }
        finally
        {
            foreach (var d in disposables) d.Dispose();
        }
    }


    // Lists the caller's group materials with uploader info (newest first).
    [HttpGet("materials")]
    public async Task<IActionResult> GetMaterials()
    {
        var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        var user = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);

        if (user?.GroupId == null)
            return BadRequest(new { message = "Ви не належите до жодної групи." });

        var materials = await _context.LectureMaterials
            .AsNoTracking()
            .Where(m => m.GroupId == user.GroupId)
            .OrderByDescending(m => m.UploadedAt)
            .Select(m => new LectureMaterialDto(
                m.Id,
                m.FileName,
                m.ContentType,
                m.SizeBytes,
                m.UploaderId,
                m.Uploader!.Username,
                m.UploadedAt))
            .ToListAsync();

        return Ok(materials);
    }


    // Returns a 5-minute presigned R2 URL so the SPA can stream the file directly from Cloudflare.
    [HttpGet("materials/{id}/download")]
    public async Task<IActionResult> GetDownloadUrl(Guid id)
    {
        var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        var user = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);

        if (user?.GroupId == null)
            return BadRequest(new { message = "Ви не належите до жодної групи." });

        var material = await _context.LectureMaterials.AsNoTracking().FirstOrDefaultAsync(m => m.Id == id);
        if (material == null) return NotFound(new { message = "Матеріал не знайдено." });
        if (material.GroupId != user.GroupId)
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "Матеріал належить іншій групі." });

        var url = _storage.GetPresignedDownloadUrl(material.R2Key, material.FileName, TimeSpan.FromMinutes(5));

        return Ok(new { url, expiresInSeconds = 300 });
    }


    // Uploader-or-Owner-only: deletes the material from R2 and the DB.
    [HttpDelete("materials/{id}")]
    public async Task<IActionResult> DeleteMaterial(Guid id)
    {
        var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        var material = await _context.LectureMaterials.Include(m => m.Group).FirstOrDefaultAsync(m => m.Id == id);

        if (material == null) return NotFound(new { message = "Матеріал не знайдено." });


        if (material.UploaderId != userId && material.Group?.OwnerId != userId)
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "Видалити може тільки той, хто завантажив, або староста групи." });

        await _storage.DeleteAsync(material.R2Key);

        _context.LectureMaterials.Remove(material);
        await _context.SaveChangesAsync();
        return Ok(new { message = "Матеріал видалено" });
    }


    // Returns the threaded comment tree for a material (flat fetch → tree built server-side).
    [HttpGet("materials/{id}/comments")]
    public async Task<IActionResult> GetComments(Guid id)
    {
        var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        var user = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);

        if (user?.GroupId == null)
            return BadRequest(new { message = "Ви не належите до жодної групи." });

        var material = await _context.LectureMaterials.AsNoTracking().FirstOrDefaultAsync(m => m.Id == id);
        if (material == null) return NotFound(new { message = "Матеріал не знайдено." });
        if (material.GroupId != user.GroupId)
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "Матеріал належить іншій групі." });

        var flat = await _context.MaterialComments
            .AsNoTracking()
            .Where(c => c.MaterialId == id)
            .OrderBy(c => c.CreatedAt)
            .Select(c => new MaterialCommentDto(
                c.Id,
                c.ParentCommentId,
                c.AuthorId,
                c.Author!.Username,
                c.Content,
                c.CreatedAt,
                new List<MaterialCommentDto>()))
            .ToListAsync();

        return Ok(BuildTree(flat));
    }


    // Posts a comment (or reply) and fans out @mention notifications to in-group recipients.
    [HttpPost("materials/{id}/comments")]
    public async Task<IActionResult> CreateComment(Guid id, [FromBody] CreateCommentDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Content))
            return BadRequest(new { message = "Коментар не може бути порожнім." });

        var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        var user = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);

        if (user?.GroupId == null)
            return BadRequest(new { message = "Ви не належите до жодної групи." });

        var material = await _context.LectureMaterials.AsNoTracking().FirstOrDefaultAsync(m => m.Id == id);
        if (material == null) return NotFound(new { message = "Матеріал не знайдено." });
        if (material.GroupId != user.GroupId)
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "Матеріал належить іншій групі." });

        if (dto.ParentCommentId.HasValue)
        {
            var parentExists = await _context.MaterialComments
                .AnyAsync(c => c.Id == dto.ParentCommentId.Value && c.MaterialId == id);

            if (!parentExists)
                return BadRequest(new { message = "Батьківський коментар не знайдено." });
        }

        var comment = new MaterialComment
        {
            Id = Guid.NewGuid(),
            MaterialId = id,
            AuthorId = userId,
            ParentCommentId = dto.ParentCommentId,
            Content = dto.Content.Trim(),
            CreatedAt = DateTime.UtcNow
        };

        _context.MaterialComments.Add(comment);
        await _context.SaveChangesAsync();


        // @mention convention shared with ChatHub — keep regex in sync.
        var usernames = System.Text.RegularExpressions.Regex
            .Matches(comment.Content, @"@(\w+)")
            .Select(m => m.Groups[1].Value)
            .Where(u => !string.Equals(u, user.Username, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (usernames.Count > 0)
        {
            var mentioned = await _context.Users
                .AsNoTracking()
                .Where(u => u.GroupId == user.GroupId && usernames.Contains(u.Username) && u.Id != userId)
                .Select(u => u.Id)
                .ToListAsync();

            if (mentioned.Count > 0)
            {
                var snippet = comment.Content.Length > 200
                    ? comment.Content.Substring(0, 200) + "…"
                    : comment.Content;
                await _notifications.NotifyManyAsync(
                    mentioned,
                    NotificationType.MentionInComment,
                    $"{user.Username} згадав вас у коментарі",
                    snippet,
                    material.Id);
            }
        }

        return Ok(new MaterialCommentDto(
            comment.Id,
            comment.ParentCommentId,
            userId,
            user.Username ?? string.Empty,
            comment.Content,
            comment.CreatedAt,
            new List<MaterialCommentDto>()));
    }


    // Author-or-Owner-only: removes a comment (cascade-deletes its replies via FK).
    [HttpDelete("comments/{commentId}")]
    public async Task<IActionResult> DeleteComment(Guid commentId)
    {
        var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        var comment = await _context.MaterialComments
            .Include(c => c.Material!).ThenInclude(m => m.Group)
            .FirstOrDefaultAsync(c => c.Id == commentId);

        if (comment == null) return NotFound(new { message = "Коментар не знайдено." });


        if (comment.AuthorId != userId && comment.Material?.Group?.OwnerId != userId)
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "Видалити може тільки автор або староста групи." });

        _context.MaterialComments.Remove(comment);
        await _context.SaveChangesAsync();
        return Ok(new { message = "Коментар видалено" });
    }


    // Rebuilds a flat comment list into a parent→replies tree in one O(n) pass.
    private static List<MaterialCommentDto> BuildTree(List<MaterialCommentDto> flat)
    {
        var byId = flat.ToDictionary(c => c.Id);
        var roots = new List<MaterialCommentDto>();

        foreach (var c in flat)
        {
            if (c.ParentCommentId.HasValue && byId.TryGetValue(c.ParentCommentId.Value, out var parent))
                parent.Replies.Add(c);
            else
                roots.Add(c);
        }

        return roots;
    }
}

// Material row returned by GET /storage/materials.
public record LectureMaterialDto(
    Guid Id,
    string FileName,
    string ContentType,
    long SizeBytes,
    Guid UploaderId,
    string UploaderName,
    DateTime UploadedAt);

// Recursive comment node used by GET /storage/materials/{id}/comments.
public record MaterialCommentDto(
    Guid Id,
    Guid? ParentCommentId,
    Guid AuthorId,
    string AuthorName,
    string Content,
    DateTime CreatedAt,
    List<MaterialCommentDto> Replies);

// Request body for POST /storage/materials/{id}/comments.
public record CreateCommentDto(
    [Required(ErrorMessage = "Текст коментаря обов'язковий")]
    [StringLength(2000, MinimumLength = 1, ErrorMessage = "Коментар має бути від 1 до 2000 символів")]
    string Content,
    Guid? ParentCommentId);
