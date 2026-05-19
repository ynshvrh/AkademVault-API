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


// IDigestAIClient fake: returns a canned response and captures the last user prompt for assertions.
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

// Tests for the AI digest endpoint: input validation, Owner gate, time window, and AI bypass when empty.
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

    // Non-Owner cannot generate a digest (403); AI is not called.
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


        var result = await controller.Generate();


        var objectResult = result.Should().BeOfType<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        ai.LastUserPrompt.Should().BeNull();
    }

    // Zero-activity window short-circuits and returns a static "no activity" summary (no LLM call).
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


        var result = await controller.Generate();


        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ai.LastUserPrompt.Should().BeNull("AI не має викликатись якщо немає подій");
        ok.Value!.GetType().GetProperty("summary")!.GetValue(ok.Value)!.ToString()
            .Should().Contain("не було жодної активності");
    }

    // Happy path: every event-source (materials/assignments/chat) ends up in the prompt and the LLM summary is returned.
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


        var result = await controller.Generate();


        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ai.LastUserPrompt.Should().NotBeNull();
        ai.LastUserPrompt!.Should().Contain("лекція_1.pdf");
        ai.LastUserPrompt.Should().Contain("Лаба 5");
        ai.LastUserPrompt.Should().Contain("Хто вже здав лабу?");

        // Server-side sanitiser normalises «- » list markers to «• ».
        var summary = ok.Value!.GetType().GetProperty("summary")!.GetValue(ok.Value)!.ToString();
        summary.Should().Be("• Завантажено лекцію\n• Створено завдання");
    }

    // 24h window excludes events older than 24 hours even if they belong to the same group.
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
            UploadedAt = now.AddHours(-6)
        });


        context.LectureMaterials.Add(new LectureMaterial
        {
            Id = Guid.NewGuid(), GroupId = groupId, UploaderId = ownerId,
            FileName = "стара.pdf", ContentType = "application/pdf", R2Key = "k2",
            UploadedAt = now.AddDays(-3)
        });

        await context.SaveChangesAsync();

        SetUser(controller, ownerId);


        var result = await controller.Generate();


        result.Should().BeOfType<OkObjectResult>();
        ai.LastUserPrompt.Should().NotBeNull();
        ai.LastUserPrompt!.Should().Contain("недавня.pdf");
        ai.LastUserPrompt.Should().NotContain("стара.pdf");
    }

    // Sanitiser strips bold/italic markers and rewrites bullet lines to a unified «• » prefix.
    [Theory]
    [InlineData("- Перший пункт\n- Другий пункт", "• Перший пункт\n• Другий пункт")]
    [InlineData("**Жирне** і *курсив*", "Жирне і курсив")]
    [InlineData("# Заголовок\n* пункт", "Заголовок\n• пункт")]
    [InlineData("Текст з `кодом` всередині", "Текст з кодом всередині")]
    public void SanitizeSummary_ShouldStripMarkdownAndNormaliseBullets(string input, string expected)
    {
        DigestController.SanitizeSummary(input).Should().Be(expected);
    }
}
