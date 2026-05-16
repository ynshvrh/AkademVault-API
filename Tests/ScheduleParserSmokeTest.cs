using Xunit;
using FluentAssertions;
using AkademVault_API.Models;
using AkademVault_API.Services;
using ClosedXML.Excel;
using DotNetEnv;

namespace Tests;


[Trait("Category", "Integration")]
public class ScheduleParserSmokeTest
{
    [Fact]
    public async Task ParseXlsx_ShouldExtractScheduleEntries()
    {
        var envPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".env"));
        Env.Load(envPath);

        var apiKey  = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY");
        var model   = Environment.GetEnvironmentVariable("OPENROUTER_MODEL") ?? "anthropic/claude-haiku-4-5";
        var baseUrl = Environment.GetEnvironmentVariable("OPENROUTER_BASE_URL") ?? "https://openrouter.ai/api/v1";

        apiKey.Should().NotBeNullOrEmpty("OPENROUTER_API_KEY має бути в .env");


        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        var openRouter = new OpenRouterClient(http, apiKey!, model, baseUrl);
        var parser = new ScheduleParser(openRouter);


        var xlsxBytes = BuildSimpleSchedule();


        var entries = await parser.ParseAsync("rozklad.xlsx",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            xlsxBytes);


        entries.Should().NotBeEmpty("AI має витягнути хоча б одну подію з таблиці");

        var algos = entries.FirstOrDefault(e => e.Title.Contains("Алгоритми", StringComparison.OrdinalIgnoreCase));
        algos.Should().NotBeNull("очікуємо подію 'Алгоритми' з таблиці");
        algos!.DayOfWeek.Should().Be(DayOfWeek.Monday);
        algos.StartTime.Should().Be(new TimeOnly(8, 30));
        algos.EndTime.Should().Be(new TimeOnly(10, 0));

        Console.WriteLine($"Витягнуто {entries.Count} події:");
        foreach (var e in entries)
            Console.WriteLine($"  {e.DayOfWeek} {e.StartTime}-{e.EndTime} {e.Type} {e.Title} ({e.Location ?? "—"})");
    }


    private static byte[] BuildSimpleSchedule()
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.AddWorksheet("Розклад");

        ws.Cell(1, 1).Value = "День";
        ws.Cell(1, 2).Value = "Початок";
        ws.Cell(1, 3).Value = "Кінець";
        ws.Cell(1, 4).Value = "Тип";
        ws.Cell(1, 5).Value = "Предмет";
        ws.Cell(1, 6).Value = "Аудиторія";

        ws.Cell(2, 1).Value = "Понеділок";
        ws.Cell(2, 2).Value = "08:30";
        ws.Cell(2, 3).Value = "10:00";
        ws.Cell(2, 4).Value = "Лекція";
        ws.Cell(2, 5).Value = "Алгоритми";
        ws.Cell(2, 6).Value = "304";

        ws.Cell(3, 1).Value = "Вівторок";
        ws.Cell(3, 2).Value = "10:30";
        ws.Cell(3, 3).Value = "12:00";
        ws.Cell(3, 4).Value = "Лабораторна";
        ws.Cell(3, 5).Value = "ООП";
        ws.Cell(3, 6).Value = "211";

        ws.Cell(4, 1).Value = "Середа";
        ws.Cell(4, 2).Value = "14:00";
        ws.Cell(4, 3).Value = "15:30";
        ws.Cell(4, 4).Value = "Семінар";
        ws.Cell(4, 5).Value = "Бази даних";
        ws.Cell(4, 6).Value = "117";

        using var ms = new MemoryStream();
        workbook.SaveAs(ms);
        return ms.ToArray();
    }
}
