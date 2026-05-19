using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AkademVault_API.Models;
using ClosedXML.Excel;
using Path = System.IO.Path;

namespace AkademVault_API.Services;

// Turns user-uploaded files (XLSX / image / PDF) into normalised ParsedScheduleEntry rows via the AI client.
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
        "ПРАВИЛА КОНВЕРТАЦІЇ:\n" +
        "- type: Лекція/Лекция → Lecture; Лабораторна/Лаба/Лабораторная/Лаб → Lab; " +
        "Семінар/Семинар → Seminar; Практика/Практ/Практичне → Practice; інше → Other. " +
        "Завжди повертай тільки АНГЛІЙСЬКОЮ.\n" +
        "- dayOfWeek: Понеділок/Пн → Monday; Вівторок/Вт → Tuesday; Середа/Ср → Wednesday; " +
        "Четвер/Чт → Thursday; П'ятниця/Пт → Friday; Субота/Сб → Saturday; Неділя/Нд → Sunday. " +
        "Завжди ТІЛЬКИ англійською.\n" +
        "Якщо неможливо визначити поле — використовуй null. " +
        "Якщо неможливо нічого витягти — поверни порожній масив [].";

    // Local fallback maps in case the AI still emits Ukrainian forms despite the prompt.
    private static readonly Dictionary<string, ScheduleEntryType> UaTypeMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Лекція"] = ScheduleEntryType.Lecture,
        ["Лекция"] = ScheduleEntryType.Lecture,
        ["Лек"] = ScheduleEntryType.Lecture,
        ["Лабораторна"] = ScheduleEntryType.Lab,
        ["Лабораторная"] = ScheduleEntryType.Lab,
        ["Лаба"] = ScheduleEntryType.Lab,
        ["Лаб"] = ScheduleEntryType.Lab,
        ["Семінар"] = ScheduleEntryType.Seminar,
        ["Семинар"] = ScheduleEntryType.Seminar,
        ["Сем"] = ScheduleEntryType.Seminar,
        ["Практика"] = ScheduleEntryType.Practice,
        ["Практичне"] = ScheduleEntryType.Practice,
        ["Практ"] = ScheduleEntryType.Practice,
    };

    private static readonly Dictionary<string, DayOfWeek> UaDayMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Понеділок"] = DayOfWeek.Monday,   ["Пн"] = DayOfWeek.Monday,
        ["Вівторок"]  = DayOfWeek.Tuesday,  ["Вт"] = DayOfWeek.Tuesday,
        ["Середа"]    = DayOfWeek.Wednesday,["Ср"] = DayOfWeek.Wednesday,
        ["Четвер"]    = DayOfWeek.Thursday, ["Чт"] = DayOfWeek.Thursday,
        ["Пʼятниця"]  = DayOfWeek.Friday,   ["П'ятниця"] = DayOfWeek.Friday, ["Пятница"] = DayOfWeek.Friday, ["Пт"] = DayOfWeek.Friday,
        ["Субота"]    = DayOfWeek.Saturday, ["Сб"] = DayOfWeek.Saturday,
        ["Неділя"]    = DayOfWeek.Sunday,   ["Нд"] = DayOfWeek.Sunday,
    };

    public ScheduleParser(IMultimodalAIClient ai) => _ai = ai;

    // Routes XLSX to a text-prompt path and image/PDF to the multimodal path, then parses the JSON.
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

    // Flattens every used row of every XLSX worksheet into a "cell | cell | …" plain-text prompt.
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

    // Tolerantly parses the AI's JSON array — silently drops malformed entries instead of throwing.
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

                var typeRaw = GetStringOrNull(el, "type") ?? "Other";
                var type = ParseEnum<ScheduleEntryType>(typeRaw)
                           ?? (UaTypeMap.TryGetValue(typeRaw.Trim(), out var t) ? t : ScheduleEntryType.Other);

                var dayRaw = GetStringOrNull(el, "dayOfWeek") ?? "";
                DayOfWeek? day = ParseEnum<DayOfWeek>(dayRaw)
                                 ?? (UaDayMap.TryGetValue(dayRaw.Trim(), out var d) ? (DayOfWeek?)d : null);
                var start = ParseTime(GetStringOrNull(el, "startTime"));
                var end = ParseTime(GetStringOrNull(el, "endTime"));

                if (day == null || start == null || end == null || end <= start) continue;

                result.Add(new ParsedScheduleEntry(
                    title!.Trim(),
                    type.ToString(),
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

    // Strips any prose around the JSON array the model returned.
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
