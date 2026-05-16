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


public class FakeDigestAIClient : IDigestAIClient
{
    public string? LastUserPrompt { get; private set; }
    public string Response { get; set; } = "- Подія A\n- Подія B";

    public Task<string> SummarizeAsync(string systemPrompt, string userPrompt, CancellationToken ct = default)
    {
        LastUserPrompt = userPrompt;
        return Task.FromResult(Response);
    }
}

public class DigestTests
{
    private AppDbContext GetDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    private static void SetUser(DigestController controller, Guid userId)
    {
        var claims = new List<Claim> { new Claim(ClaimTypes.NameIdentifier, userId.ToString()) };
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims)) }
        };
    }

    [Fact]
    public async Task Generate_ShouldReturnBadRequest_WhenPeriodInvalid()
    {

        var context = GetDbContext();
        var ai = new FakeDigestAIClient();
        var controller = new DigestController(context, ai, new FakeNotificationService());

        var ownerId = Guid.NewGuid();
        context.Groups.Add(new Group { Id = Guid.NewGuid(), Name = "КН-31", OwnerId = ownerId });
        await context.SaveChangesAsync();

        SetUser(controller, ownerId);


        var result = await controller.Generate("week");


        result.Should().BeOfType<BadRequestObjectResult>();
        ai.LastUserPrompt.Should().BeNull();
    }

    [Fact]
    public async Task Generate_ShouldReturnForbid_WhenUserIsNotOwner()
    {

        var context = GetDbContext();
        var ai = new FakeDigestAIClient();
        var controller = new DigestController(context, ai, new FakeNotificationService());

        var ownerId = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        var groupId = Guid.NewGuid();

        context.Groups.Add(new Group { Id = groupId, Name = "КН-31", OwnerId = ownerId });
        context.Users.Add(new User { Id = memberId, GroupId = groupId, Username = "звичайний студент" });
        await context.SaveChangesAsync();

        SetUser(controller, memberId);


        var result = await controller.Generate("day");


        var objectResult = result.Should().BeOfType<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        ai.LastUserPrompt.Should().BeNull();
    }

    [Fact]
    public async Task Generate_ShouldSkipAI_WhenNoEvents()
    {

        var context = GetDbContext();
        var ai = new FakeDigestAIClient();
        var controller = new DigestController(context, ai, new FakeNotificationService());

        var ownerId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        context.Groups.Add(new Group { Id = groupId, Name = "КН-31", OwnerId = ownerId });
        await context.SaveChangesAsync();

        SetUser(controller, ownerId);


        var result = await controller.Generate("day");


        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ai.LastUserPrompt.Should().BeNull("AI не має викликатись якщо немає подій");
        ok.Value!.GetType().GetProperty("summary")!.GetValue(ok.Value)!.ToString()
            .Should().Contain("не було жодної активності");
    }

    [Fact]
    public async Task Generate_ShouldPassEventsToAI_AndReturnSummary()
    {

        var context = GetDbContext();
        var ai = new FakeDigestAIClient { Response = "- Завантажено лекцію\n- Створено завдання" };
        var controller = new DigestController(context, ai, new FakeNotificationService());

        var ownerId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        context.Groups.Add(new Group { Id = groupId, Name = "КН-31", OwnerId = ownerId });
        context.Users.Add(new User { Id = ownerId, GroupId = groupId, Username = "yanosh_dev" });

        context.LectureMaterials.Add(new LectureMaterial
        {
            Id = Guid.NewGuid(), GroupId = groupId, UploaderId = ownerId,
            FileName = "лекція_1.pdf", ContentType = "application/pdf", R2Key = "k",
            UploadedAt = now.AddMinutes(-30)
        });

        context.Assignments.Add(new Assignment
        {
            Id = Guid.NewGuid(), GroupId = groupId,
            Title = "Лаба 5", Description = "Зробити SignalR-чат",
            DueDate = now.AddDays(7), CreatedAt = now.AddMinutes(-20)
        });

        context.ChatMessages.Add(new ChatMessage
        {
            Id = Guid.NewGuid(), GroupId = groupId, SenderId = ownerId,
            Content = "Хто вже здав лабу?", SentAt = now.AddMinutes(-10)
        });

        await context.SaveChangesAsync();

        SetUser(controller, ownerId);


        var result = await controller.Generate("day");


        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ai.LastUserPrompt.Should().NotBeNull();
        ai.LastUserPrompt!.Should().Contain("лекція_1.pdf");
        ai.LastUserPrompt.Should().Contain("Лаба 5");
        ai.LastUserPrompt.Should().Contain("Хто вже здав лабу?");

        var summary = ok.Value!.GetType().GetProperty("summary")!.GetValue(ok.Value)!.ToString();
        summary.Should().Be("- Завантажено лекцію\n- Створено завдання");
    }

    [Fact]
    public async Task Generate_ShouldRespectTimeWindow()
    {

        var context = GetDbContext();
        var ai = new FakeDigestAIClient();
        var controller = new DigestController(context, ai, new FakeNotificationService());

        var ownerId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        context.Groups.Add(new Group { Id = groupId, Name = "КН-31", OwnerId = ownerId });
        context.Users.Add(new User { Id = ownerId, GroupId = groupId, Username = "yanosh_dev" });


        context.LectureMaterials.Add(new LectureMaterial
        {
            Id = Guid.NewGuid(), GroupId = groupId, UploaderId = ownerId,
            FileName = "недавня.pdf", ContentType = "application/pdf", R2Key = "k1",
            UploadedAt = now.AddMinutes(-30)
        });


        context.LectureMaterials.Add(new LectureMaterial
        {
            Id = Guid.NewGuid(), GroupId = groupId, UploaderId = ownerId,
            FileName = "стара.pdf", ContentType = "application/pdf", R2Key = "k2",
            UploadedAt = now.AddHours(-3)
        });

        await context.SaveChangesAsync();

        SetUser(controller, ownerId);


        var result = await controller.Generate("hour");


        result.Should().BeOfType<OkObjectResult>();
        ai.LastUserPrompt.Should().NotBeNull();
        ai.LastUserPrompt!.Should().Contain("недавня.pdf");
        ai.LastUserPrompt.Should().NotContain("стара.pdf");
    }
}