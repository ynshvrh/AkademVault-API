using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AkademVault_API.Data;
using AkademVault_API.Models;
using AkademVault_API.Services;
using System.Security.Claims;

namespace AkademVault_API.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class StorageController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly IR2StorageService _storage;

    private static readonly long MaxFileSize = 25 * 1024 * 1024;

    private static readonly Dictionary<string, string> AllowedTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"]  = "application/pdf",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".webp"] = "image/webp"
    };

    public StorageController(AppDbContext context, IR2StorageService storage)
    {
        _context = context;
        _storage = storage;
    }


    [HttpPost("upload")]
    [RequestSizeLimit(26_214_400)]
    public async Task<IActionResult> Upload(IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest("Файл не надано.");

        if (file.Length > MaxFileSize)
            return BadRequest("Файл перевищує ліміт 25 MB.");

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllowedTypes.TryGetValue(extension, out var expectedContentType))
            return BadRequest("Дозволені формати: .pdf, .docx, .webp");

        if (!string.Equals(file.ContentType, expectedContentType, StringComparison.OrdinalIgnoreCase))
            return BadRequest("Тип файлу не відповідає його розширенню.");

        var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        var user = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);

        if (user?.GroupId == null)
            return BadRequest("Ви не належите до жодної групи.");

        var materialId = Guid.NewGuid();
        var key = $"groups/{user.GroupId}/{materialId}{extension}";

        await using (var stream = file.OpenReadStream())
        {
            await _storage.UploadAsync(key, stream, expectedContentType);
        }

        var material = new LectureMaterial
        {
            Id = materialId,
            GroupId = user.GroupId.Value,
            UploaderId = userId,
            FileName = Path.GetFileName(file.FileName),
            ContentType = expectedContentType,
            SizeBytes = file.Length,
            R2Key = key,
            UploadedAt = DateTime.UtcNow
        };

        _context.LectureMaterials.Add(material);
        await _context.SaveChangesAsync();

        return Ok(new
        {
            material.Id,
            material.FileName,
            material.ContentType,
            material.SizeBytes,
            material.UploadedAt
        });
    }


    [HttpGet("materials")]
    public async Task<IActionResult> GetMaterials()
    {
        var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        var user = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);

        if (user?.GroupId == null)
            return BadRequest("Ви не належите до жодної групи.");

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


    [HttpGet("materials/{id}/download")]
    public async Task<IActionResult> GetDownloadUrl(Guid id)
    {
        var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        var user = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);

        if (user?.GroupId == null)
            return BadRequest("Ви не належите до жодної групи.");

        var material = await _context.LectureMaterials.AsNoTracking().FirstOrDefaultAsync(m => m.Id == id);
        if (material == null) return NotFound();
        if (material.GroupId != user.GroupId) return Forbid();

        var url = _storage.GetPresignedDownloadUrl(material.R2Key, material.FileName, TimeSpan.FromMinutes(5));

        return Ok(new { url, expiresInSeconds = 300 });
    }


    [HttpDelete("materials/{id}")]
    public async Task<IActionResult> DeleteMaterial(Guid id)
    {
        var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        var material = await _context.LectureMaterials.Include(m => m.Group).FirstOrDefaultAsync(m => m.Id == id);

        if (material == null) return NotFound();


        if (material.UploaderId != userId && material.Group?.OwnerId != userId)
            return Forbid();

        await _storage.DeleteAsync(material.R2Key);

        _context.LectureMaterials.Remove(material);
        await _context.SaveChangesAsync();
        return Ok("Матеріал видалено");
    }


    [HttpGet("materials/{id}/comments")]
    public async Task<IActionResult> GetComments(Guid id)
    {
        var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        var user = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);

        if (user?.GroupId == null)
            return BadRequest("Ви не належите до жодної групи.");

        var material = await _context.LectureMaterials.AsNoTracking().FirstOrDefaultAsync(m => m.Id == id);
        if (material == null) return NotFound();
        if (material.GroupId != user.GroupId) return Forbid();

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


    [HttpPost("materials/{id}/comments")]
    public async Task<IActionResult> CreateComment(Guid id, [FromBody] CreateCommentDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Content))
            return BadRequest("Коментар не може бути порожнім.");

        var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        var user = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);

        if (user?.GroupId == null)
            return BadRequest("Ви не належите до жодної групи.");

        var material = await _context.LectureMaterials.AsNoTracking().FirstOrDefaultAsync(m => m.Id == id);
        if (material == null) return NotFound();
        if (material.GroupId != user.GroupId) return Forbid();

        if (dto.ParentCommentId.HasValue)
        {
            var parentExists = await _context.MaterialComments
                .AnyAsync(c => c.Id == dto.ParentCommentId.Value && c.MaterialId == id);

            if (!parentExists)
                return BadRequest("Батьківський коментар не знайдено.");
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

        return Ok(new MaterialCommentDto(
            comment.Id,
            comment.ParentCommentId,
            userId,
            user.Username ?? string.Empty,
            comment.Content,
            comment.CreatedAt,
            new List<MaterialCommentDto>()));
    }


    [HttpDelete("comments/{commentId}")]
    public async Task<IActionResult> DeleteComment(Guid commentId)
    {
        var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        var comment = await _context.MaterialComments
            .Include(c => c.Material!).ThenInclude(m => m.Group)
            .FirstOrDefaultAsync(c => c.Id == commentId);

        if (comment == null) return NotFound();


        if (comment.AuthorId != userId && comment.Material?.Group?.OwnerId != userId)
            return Forbid();

        _context.MaterialComments.Remove(comment);
        await _context.SaveChangesAsync();
        return Ok("Коментар видалено");
    }


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

public record LectureMaterialDto(
    Guid Id,
    string FileName,
    string ContentType,
    long SizeBytes,
    Guid UploaderId,
    string UploaderName,
    DateTime UploadedAt);

public record MaterialCommentDto(
    Guid Id,
    Guid? ParentCommentId,
    Guid AuthorId,
    string AuthorName,
    string Content,
    DateTime CreatedAt,
    List<MaterialCommentDto> Replies);

public record CreateCommentDto(string Content, Guid? ParentCommentId);
