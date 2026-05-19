using Xunit;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using AkademVault_API.Controllers;
using AkademVault_API.Data;
using AkademVault_API.Models;
using AkademVault_API.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;

namespace Tests;


// IScheduleParser fake: returns a pre-set list of entries without touching the AI service.
public class FakeScheduleParser : IScheduleParser
{
    public List<ParsedScheduleEntry> NextResult { get; set; } = new();

    public Task<List<ParsedScheduleEntry>> ParseAsync(string fileName, string contentType, byte[] data, CancellationToken ct = default)
        => Task.FromResult(NextResult);
}

// Tests for the schedule CRUD endpoints and the AI Parse/Confirm flow.
public class ScheduleTests
{
    private AppDbContext GetDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    private static void SetUser(ScheduleController controller, Guid userId)
    {
        var claims = new List<Claim> { new Claim(ClaimTypes.NameIdentifier, userId.ToString()) };
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims)) }
        };
    }

    // Non-Owner cannot create schedule entries (403).
    [Fact]
    public async Task Create_ShouldReturnForbid_WhenNotOwner()
    {
        var context = GetDbContext();
        var controller = new ScheduleController(context, new FakeScheduleParser());
        var userId = Guid.NewGuid();
        SetUser(controller, userId);

        var dto = new ScheduleEntryWriteDto("Алгоритми", ScheduleEntryType.Lecture, DayOfWeek.Monday,
            new TimeOnly(8, 30), new TimeOnly(10, 0), "ауд. 304", "Іваненко І.");

        var result = await controller.Create(dto);

        var __fr = result.Should().BeOfType<ObjectResult>().Subject; __fr.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    // Owner can create a valid entry and it is persisted.
    [Fact]
    public async Task Create_ShouldSucceed_ForOwner()
    {
        var context = GetDbContext();
        var controller = new ScheduleController(context, new FakeScheduleParser());
        var ownerId = Guid.NewGuid();
        var groupId = Guid.NewGuid();

        context.Groups.Add(new Group { Id = groupId, Name = "КН-31", ShortCode = "TST-0001", OwnerId = ownerId });
        await context.SaveChangesAsync();

        SetUser(controller, ownerId);

        var dto = new ScheduleEntryWriteDto("Алгоритми", ScheduleEntryType.Lecture, DayOfWeek.Monday,
            new TimeOnly(8, 30), new TimeOnly(10, 0), "ауд. 304", "Іваненко І.");

        var result = await controller.Create(dto);

        result.Should().BeOfType<OkObjectResult>();
        (await context.ScheduleEntries.CountAsync()).Should().Be(1);
    }

    // Time-window validation: EndTime ≤ StartTime is rejected with 400.
    [Fact]
    public async Task Create_ShouldReturnBadRequest_WhenEndBeforeStart()
    {
        var context = GetDbContext();
        var controller = new ScheduleController(context, new FakeScheduleParser());
        var ownerId = Guid.NewGuid();
        var groupId = Guid.NewGuid();

        context.Groups.Add(new Group { Id = groupId, Name = "КН-31", ShortCode = "TST-0001", OwnerId = ownerId });
        await context.SaveChangesAsync();

        SetUser(controller, ownerId);

        var dto = new ScheduleEntryWriteDto("Тест", ScheduleEntryType.Lecture, DayOfWeek.Tuesday,
            new TimeOnly(12, 0), new TimeOnly(10, 0), null, null);

        var result = await controller.Create(dto);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    // GetAll returns only entries that belong to the caller's group.
    [Fact]
    public async Task GetAll_ShouldReturnOnlyGroupEntries()
    {
        var context = GetDbContext();
        var controller = new ScheduleController(context, new FakeScheduleParser());
        var memberId = Guid.NewGuid();
        var groupId = Guid.NewGuid();

        context.Groups.Add(new Group { Id = groupId, Name = "КН-31", ShortCode = "TST-0001", OwnerId = Guid.NewGuid() });
        context.Users.Add(new User { Id = memberId, Username = "student", GroupId = groupId });

        context.ScheduleEntries.Add(new ScheduleEntry
        {
            Id = Guid.NewGuid(), GroupId = groupId, Title = "Своє", Type = ScheduleEntryType.Lecture,
            DayOfWeek = DayOfWeek.Monday, StartTime = new TimeOnly(8, 30), EndTime = new TimeOnly(10, 0)
        });

        context.ScheduleEntries.Add(new ScheduleEntry
        {
            Id = Guid.NewGuid(), GroupId = Guid.NewGuid(), Title = "Чуже", Type = ScheduleEntryType.Lecture,
            DayOfWeek = DayOfWeek.Monday, StartTime = new TimeOnly(8, 30), EndTime = new TimeOnly(10, 0)
        });

        await context.SaveChangesAsync();

        SetUser(controller, memberId);

        var result = await controller.GetAll();

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var entries = ok.Value as List<ScheduleEntryDto>;
        entries.Should().HaveCount(1);
        entries!.First().Title.Should().Be("Своє");
    }

    // Confirm bulk-persists every well-formed entry in one transaction.
    [Fact]
    public async Task Confirm_ShouldBulkInsertEntries()
    {
        var context = GetDbContext();
        var controller = new ScheduleController(context, new FakeScheduleParser());
        var ownerId = Guid.NewGuid();
        var groupId = Guid.NewGuid();

        context.Groups.Add(new Group { Id = groupId, Name = "КН-31", ShortCode = "TST-0001", OwnerId = ownerId });
        await context.SaveChangesAsync();

        SetUser(controller, ownerId);

        var entries = new List<ScheduleEntryWriteDto>
        {
            new("Алгоритми", ScheduleEntryType.Lecture, DayOfWeek.Monday, new TimeOnly(8, 30), new TimeOnly(10, 0), "304", "Іваненко"),
            new("ООП", ScheduleEntryType.Lab, DayOfWeek.Tuesday, new TimeOnly(10, 30), new TimeOnly(12, 0), "211", null)
        };

        var result = await controller.Confirm(entries);

        result.Should().BeOfType<OkObjectResult>();
        (await context.ScheduleEntries.CountAsync()).Should().Be(2);
    }

    // Confirm silently drops entries with invalid time windows instead of failing the whole batch.
    [Fact]
    public async Task Confirm_ShouldSkipEntriesWithInvalidTime()
    {
        var context = GetDbContext();
        var controller = new ScheduleController(context, new FakeScheduleParser());
        var ownerId = Guid.NewGuid();
        var groupId = Guid.NewGuid();

        context.Groups.Add(new Group { Id = groupId, Name = "КН-31", ShortCode = "TST-0001", OwnerId = ownerId });
        await context.SaveChangesAsync();

        SetUser(controller, ownerId);

        var entries = new List<ScheduleEntryWriteDto>
        {
            new("Валідна", ScheduleEntryType.Lecture, DayOfWeek.Monday, new TimeOnly(8, 30), new TimeOnly(10, 0), null, null),
            new("Невалідна", ScheduleEntryType.Lab, DayOfWeek.Tuesday, new TimeOnly(14, 0), new TimeOnly(12, 0), null, null)
        };

        var result = await controller.Confirm(entries);

        result.Should().BeOfType<OkObjectResult>();
        (await context.ScheduleEntries.CountAsync()).Should().Be(1);
    }

    // Non-Owner cannot invoke the AI parse endpoint (403).
    [Fact]
    public async Task Parse_ShouldReturnForbid_WhenNotOwner()
    {
        var context = GetDbContext();
        var parser = new FakeScheduleParser();
        var controller = new ScheduleController(context, parser);
        var userId = Guid.NewGuid();
        SetUser(controller, userId);

        var file = new FormFile(new MemoryStream(new byte[] { 1, 2, 3 }), 0, 3, "file", "rozk.pdf")
        {
            Headers = new HeaderDictionary(),
            ContentType = "application/pdf"
        };

        var result = await controller.Parse(file, default);
        var __fr = result.Should().BeOfType<ObjectResult>().Subject; __fr.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    // Owner gets back whatever the parser produced (entries are not persisted yet, only previewed).
    [Fact]
    public async Task Parse_ShouldReturnEntries_ForOwner()
    {
        var context = GetDbContext();
        var parser = new FakeScheduleParser
        {
            NextResult = new List<ParsedScheduleEntry>
            {
                new("Алгоритми", "Lecture", DayOfWeek.Monday, new TimeOnly(8, 30), new TimeOnly(10, 0), "304", "Іваненко")
            }
        };
        var controller = new ScheduleController(context, parser);
        var ownerId = Guid.NewGuid();
        var groupId = Guid.NewGuid();

        context.Groups.Add(new Group { Id = groupId, Name = "КН-31", ShortCode = "TST-0001", OwnerId = ownerId });
        await context.SaveChangesAsync();

        SetUser(controller, ownerId);

        var file = new FormFile(new MemoryStream(new byte[] { 1, 2, 3 }), 0, 3, "file", "rozk.pdf")
        {
            Headers = new HeaderDictionary(),
            ContentType = "application/pdf"
        };

        var result = await controller.Parse(file, default);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var entries = ok.Value as List<ParsedScheduleEntry>;
        entries.Should().HaveCount(1);
        entries!.First().Title.Should().Be("Алгоритми");
    }

    // MIME-type allow-list: octet-stream upload returns 400 without calling the AI.
    [Fact]
    public async Task Parse_ShouldReturnBadRequest_ForDisallowedMime()
    {
        var context = GetDbContext();
        var controller = new ScheduleController(context, new FakeScheduleParser());
        var ownerId = Guid.NewGuid();

        context.Groups.Add(new Group { Id = Guid.NewGuid(), Name = "КН-31", ShortCode = "TST-0001", OwnerId = ownerId });
        await context.SaveChangesAsync();

        SetUser(controller, ownerId);

        var file = new FormFile(new MemoryStream(new byte[] { 1, 2, 3 }), 0, 3, "file", "rozk.exe")
        {
            Headers = new HeaderDictionary(),
            ContentType = "application/octet-stream"
        };

        var result = await controller.Parse(file, default);
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    // Delete of an entry that belongs to another group is forbidden even for an Owner.
    [Fact]
    public async Task Delete_ShouldReturnForbid_WhenEntryInAnotherGroup()
    {
        var context = GetDbContext();
        var controller = new ScheduleController(context, new FakeScheduleParser());
        var ownerId = Guid.NewGuid();
        var myGroupId = Guid.NewGuid();
        var otherGroupId = Guid.NewGuid();
        var entryId = Guid.NewGuid();

        context.Groups.Add(new Group { Id = myGroupId, Name = "Моя", ShortCode = "TST-0001", OwnerId = ownerId });
        context.ScheduleEntries.Add(new ScheduleEntry
        {
            Id = entryId, GroupId = otherGroupId, Title = "Чуже", Type = ScheduleEntryType.Lecture,
            DayOfWeek = DayOfWeek.Monday, StartTime = new TimeOnly(8, 30), EndTime = new TimeOnly(10, 0)
        });
        await context.SaveChangesAsync();

        SetUser(controller, ownerId);

        var result = await controller.Delete(entryId);

        var __fr = result.Should().BeOfType<ObjectResult>().Subject; __fr.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        (await context.ScheduleEntries.CountAsync()).Should().Be(1);
    }
}
