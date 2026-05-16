using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AkademVault_API.Models;

public class MaterialComment
{
    public Guid Id { get; set; }

    [Required]
    public Guid MaterialId { get; set; }

    [ForeignKey("MaterialId")]
    public LectureMaterial? Material { get; set; }

    [Required]
    public Guid AuthorId { get; set; }

    [ForeignKey("AuthorId")]
    public User? Author { get; set; }


    public Guid? ParentCommentId { get; set; }

    [ForeignKey("ParentCommentId")]
    public MaterialComment? ParentComment { get; set; }

    public ICollection<MaterialComment> Replies { get; set; } = new List<MaterialComment>();

    [Required]
    [StringLength(2000)]
    public string Content { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
