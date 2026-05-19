using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AkademVault_API.Models;

// Tenant unit of the app; OwnerId designates the group leader (starosta) with elevated permissions.
public class Group
{
    public Guid Id { get; set; }

    [Required]
    [StringLength(50)]
    public string Name { get; set; } = string.Empty;


    [Required]
    [StringLength(16)]
    public string ShortCode { get; set; } = string.Empty;

    [Required]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Required]
    public DateTime ExpiryDate { get; set; }


    [Required]
    public Guid OwnerId { get; set; }


    [ForeignKey("OwnerId")]
    public User? Owner { get; set; }


    public ICollection<User> Members { get; set; } = new List<User>();


    // Cached last AI digest — read by the dashboard for the Owner so we don't re-call the LLM on every page load.
    public string? LastDigestSummary { get; set; }
    public DateTime? LastDigestGeneratedAt { get; set; }
    public int? LastDigestMaterialCount { get; set; }
    public int? LastDigestAssignmentCount { get; set; }
    public int? LastDigestMessageCount { get; set; }
}
