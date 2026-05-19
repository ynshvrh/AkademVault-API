using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AkademVault_API.Models;

// Metadata for a file uploaded to R2; R2Key is the object's path in the bucket.
public class LectureMaterial
{
    public Guid Id { get; set; }

    [Required]
    public Guid GroupId { get; set; }

    [ForeignKey("GroupId")]
    public Group? Group { get; set; }

    [Required]
    public Guid UploaderId { get; set; }

    [ForeignKey("UploaderId")]
    public User? Uploader { get; set; }

    [Required]
    [StringLength(255)]
    public string FileName { get; set; } = string.Empty;

    [Required]
    [StringLength(100)]
    public string ContentType { get; set; } = string.Empty;

    [Required]
    public long SizeBytes { get; set; }


    [Required]
    [StringLength(512)]
    public string R2Key { get; set; } = string.Empty;

    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;

    public ICollection<MaterialComment> Comments { get; set; } = new List<MaterialComment>();
}
