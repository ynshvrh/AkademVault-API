namespace AkademVault_API.Models;

// Lifecycle of a user-initiated join request.
public enum RequestStatus { Pending, Approved, Rejected }

// User-initiated request to join a group; awaiting Owner's approve/reject decision.
public class JoinRequest
{
    public Guid Id { get; set; }
    public Guid GroupId { get; set; }
    public Guid UserId { get; set; }
    public RequestStatus Status { get; set; } = RequestStatus.Pending;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;


    public Group? Group { get; set; }
    public User? User { get; set; }
}
