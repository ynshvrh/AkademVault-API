using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AkademVault_API.Models;

// Single message in a group's chat; Reads collects per-user read-receipts.
public class ChatMessage
{
    public Guid Id { get; set; }

    [Required]
    public Guid GroupId { get; set; }

    [ForeignKey("GroupId")]
    public Group? Group { get; set; }

    [Required]
    public Guid SenderId { get; set; }

    [ForeignKey("SenderId")]
    public User? Sender { get; set; }

    [Required]
    [StringLength(2000)]
    public string Content { get; set; } = string.Empty;

    public DateTime SentAt { get; set; } = DateTime.UtcNow;

    public ICollection<MessageRead> Reads { get; set; } = new List<MessageRead>();
}
