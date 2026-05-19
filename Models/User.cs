using System.ComponentModel.DataAnnotations.Schema;
namespace AkademVault_API.Models;

// Auth principal; a user belongs to at most one group at a time (nullable GroupId).
public class User
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Guid? GroupId { get; set; }

    [ForeignKey("GroupId")]
    public Group? Group { get; set; }
}
