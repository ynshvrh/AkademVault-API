using AkademVault_API.Models;

namespace AkademVault_API.Services;

// AI-extracted schedule entry; Type is a string so the SPA can use it without mapping the enum.
public record ParsedScheduleEntry(
    string Title,
    string Type,
    DayOfWeek DayOfWeek,
    TimeOnly StartTime,
    TimeOnly EndTime,
    string? Location,
    string? Teacher);

// Schedule-import contract: accepts a raw file blob, returns the parsed entries (no DB writes).
public interface IScheduleParser
{
    // Detects the format and asks the AI to convert it into ParsedScheduleEntry rows.
    Task<List<ParsedScheduleEntry>> ParseAsync(string fileName, string contentType, byte[] data, CancellationToken ct = default);
}
