using AkademVault_API.Models;

namespace AkademVault_API.Services;

public record ParsedScheduleEntry(
    string Title,
    ScheduleEntryType Type,
    DayOfWeek DayOfWeek,
    TimeOnly StartTime,
    TimeOnly EndTime,
    string? Location,
    string? Teacher);

public interface IScheduleParser
{
    Task<List<ParsedScheduleEntry>> ParseAsync(string fileName, string contentType, byte[] data, CancellationToken ct = default);
}
