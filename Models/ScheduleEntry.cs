using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AkademVault_API.Models;

public enum ScheduleEntryType
{
    Lecture = 1,
    Lab = 2,
    Seminar = 3,
    Practice = 4,
    Other = 5
}

public class ScheduleEntry
{
    public Guid Id { get; set; }

    [Required]
    public Guid GroupId { get; set; }

    [ForeignKey("GroupId")]
    public Group? Group { get; set; }

    [Required]
    [StringLength(150)]
    public string Title { get; set; } = string.Empty;

    [Required]
    public ScheduleEntryType Type { get; set; }

    [Required]
    public DayOfWeek DayOfWeek { get; set; }


    [Required]
    public TimeOnly StartTime { get; set; }

    [Required]
    public TimeOnly EndTime { get; set; }

    [StringLength(100)]
    public string? Location { get; set; }

    [StringLength(100)]
    public string? Teacher { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
