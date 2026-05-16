using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AkademVault_API.Models;

public enum InvitationStatus { Pending, Accepted, Declined, Expired }

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
