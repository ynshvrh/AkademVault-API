using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AkademVault_API.Models;

// Lifecycle of a personal invitation; only Pending entries are actionable in the inbox.
public enum InvitationStatus { Pending, Accepted, Declined, Expired }

// Owner-issued personal invitation to a specific user, separate from shareable invite links.
public class Invitation
{
    public Guid Id { get; set; }

    [Required]
    public Guid GroupId { get; set; }

    [ForeignKey("GroupId")]
    public Group? Group { get; set; }

    [Required]
    public Guid InvitedUserId { get; set; }

    [ForeignKey("InvitedUserId")]
    public User? InvitedUser { get; set; }

    [Required]
    public Guid InvitedByUserId { get; set; }

    [ForeignKey("InvitedByUserId")]
    public User? InvitedBy { get; set; }

    [Required]
    public InvitationStatus Status { get; set; } = InvitationStatus.Pending;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? RespondedAt { get; set; }
}
