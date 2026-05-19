using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AkademVault_API.Models;

// Owner-authored homework/task with a due date, visible to the whole group.
public class Assignment
{
    public Guid Id { get; set; }

    [Required]
    [StringLength(100)]
    public string Title { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    [Required]
    public DateTime DueDate { get; set; }

    [Required]
    public Guid GroupId { get; set; }

    [ForeignKey("GroupId")]
    public Group? Group { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
