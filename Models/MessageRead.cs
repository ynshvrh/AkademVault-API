using System.ComponentModel.DataAnnotations.Schema;

namespace AkademVault_API.Models;

// Join row for per-user read-receipts on a ChatMessage; composite PK (MessageId, UserId) enforced via FluentAPI.
public class MessageRead
{
    public Guid MessageId { get; set; }

    [ForeignKey("MessageId")]
    public ChatMessage? Message { get; set; }

    public Guid UserId { get; set; }

    [ForeignKey("UserId")]
    public User? User { get; set; }

    public DateTime ReadAt { get; set; } = DateTime.UtcNow;
}
