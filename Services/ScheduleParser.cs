using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AkademVault_API.Models;
using ClosedXML.Excel;

namespace AkademVault_API.Services;

public class ScheduleParser : IScheduleParser
{
    private readonly IMultimodalAIClient _ai;

    private const string SystemPrompt =
        "Ти витягуєш розклад занять для університетської групи. " +
        "Поверни ВИКЛЮЧНО валідний JSON-масив (без markdown, без пояснень) виду: " +
        "[{\"title\":\"...\",\"type\":\"Lecture|Lab|Seminar|Practice|Other\"," +
        "\"dayOfWeek\":\"Monday|Tuesday|...|Sunday\"," +
        "\"startTime\":\"HH:mm\",\"endTime\":\"HH:mm\"," +
        "\"location\":\"...|null\",\"teacher\":\"...|null\"}]. " +
        "Якщо неможливо визначити поле — використовуй null. " +
        "Якщо неможливо нічого витягти — поверни порожній масив [].";

    public ScheduleParser(IMultimodalAIClient ai) => _ai = ai;

    public async Task<List<ParsedScheduleEntry>> ParseAsync(string fileName, string contentType, byte[] data, CancellationToken ct = default)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        string aiResponse;

        if (ext == ".xlsx" || contentType.Contains("spreadsheet", StringComparison.OrdinalIgnoreCase))
        {
            var asText = ExtractXlsxAsText(data);
            aiResponse = await _ai.CallAsync(SystemPrompt,
                $"Витягни розклад з цієї таблиці:\n\n{asText}",
                Array.Empty<MultimodalAttachment>(), ct);
        }
        else if (contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
                 || contentType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase))
        {
            aiResponse = await _ai.CallAsync(SystemPrompt,
                "Витягни розклад з прикріпленого файлу.",
                new[] { new MultimodalAttachment(contentType, data) }, ct);
        }
        else
        {
            throw new InvalidOperationException($"Непідтримуваний формат файлу: {contentType} ({ext})");
        }

        return ParseAIJson(aiResponse);
    }

    private static string ExtractXlsxAsText(byte[] data)
    {
        using var stream = new MemoryStream(data);
        using var workbook = new XLWorkbook(stream);
        var sb = new StringBuilder();

        foreach (var ws in workbook.Worksheets)
        {
            sb.AppendLine($"## Аркуш: {ws.Name}");
            var range = ws.RangeUsed();
            if (range == null) continue;

            foreach (var row in range.RowsUsed())
            {
                var cells = row.Cells().Select(c => c.GetString().Trim()).Where(s => s.Length > 0);
                var line = string.Join(" | ", cells);
                if (!string.IsNullOrWhiteSpace(line))
                    sb.AppendLine(line);
            }
        }

        return sb.ToString();
    }

    private static List<ParsedScheduleEntry> ParseAIJson(string aiResponse)
    {
        var jsonText = ExtractJsonArray(aiResponse);
        if (string.IsNullOrWhiteSpace(jsonText)) return new List<ParsedScheduleEntry>();

        using var doc = JsonDocument.Parse(jsonText);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return new List<ParsedScheduleEntry>();

        var result = new List<ParsedScheduleEntry>();

        foreach (var el in doc.RootElement.EnumerateArray())
        {
            try
            {
                var title = GetStringOrNull(el, "title");
                if (string.IsNullOrWhiteSpace(title)) continue;

                var type = ParseEnum<ScheduleEntryType>(GetStringOrNull(el, "type") ?? "Other") ?? ScheduleEntryType.Other;
                var day = ParseEnum<DayOfWeek>(GetStringOrNull(el, "dayOfWeek") ?? "");
                var start = ParseTime(GetStringOrNull(el, "startTime"));
                var end = ParseTime(GetStringOrNull(el, "endTime"));

                if (day == null || start == null || end == null || end <= start) continue;

                result.Add(new ParsedScheduleEntry(
                    title!.Trim(),
                    type,
                    day.Value,
                    start.Value,
                    end.Value,
                    GetStringOrNull(el, "location"),
                    GetStringOrNull(el, "teacher")));
            }
            catch
            {

                continue;
            }
        }

        return result;
    }

    private static string? ExtractJsonArray(string raw)
    {
        var match = Regex.Match(raw, @"\[[\s\S]*\]");
        return match.Success ? match.Value : null;
    }

    private static string? GetStringOrNull(JsonElement el, string key)
    {
        if (!el.TryGetProperty(key, out var v)) return null;
        if (v.ValueKind == JsonValueKind.Null) return null;
        if (v.ValueKind != JsonValueKind.String) return null;
        var s = v.GetString();
        return string.IsNullOrWhiteSpace(s) ? null : s;
    }

    private static T? ParseEnum<T>(string s) where T : struct, Enum
        => Enum.TryParse<T>(s, ignoreCase: true, out var result) ? result : null;

    private static TimeOnly? ParseTime(string? s)
        => string.IsNullOrWhiteSpace(s) ? null
           : TimeOnly.TryParse(s, out var t) ? t : null;
}
