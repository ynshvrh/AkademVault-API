namespace AkademVault_API.Models;

// Статуси заявки: Очікує, Схвалено, Відхилено
public enum RequestStatus { Pending, Approved, Rejected }

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