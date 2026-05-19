using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AkademVault_API.Models;

// Discriminator for the inbox; ordering matches Type column in the DB (do not renumber).
public enum NotificationType
{
    MentionInChat = 1,
    MaterialUploaded = 2,
    GroupInvitation = 3,
    DigestPublished = 4,
    JoinRequestApproved = 5,
    JoinRequestRejected = 6,
    MentionInComment = 7
}

// Persistent per-user inbox row; RelatedEntityId lets the SPA deep-link to the source entity.
public class Notification
{
    public Guid Id { get; set; }

    [Required]
    public Guid UserId { get; set; }

    [ForeignKey("UserId")]
    public User? User { get; set; }

    [Required]
    public NotificationType Type { get; set; }

    [Required]
    [StringLength(200)]
    public string Title { get; set; } = string.Empty;

    [Required]
    [StringLength(500)]
    public string Body { get; set; } = string.Empty;


    public Guid? RelatedEntityId { get; set; }

    public bool IsRead { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
