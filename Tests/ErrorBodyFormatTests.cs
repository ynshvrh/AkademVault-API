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


public class ErrorBodyFormatTests
{
    private AppDbContext GetDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    private static void SetUser(ControllerBase controller, Guid userId)
    {
        var claims = new List<Claim> { new Claim(ClaimTypes.NameIdentifier, userId.ToString()) };
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims)) }
        };
    }

    private static string? ExtractMessage(object? body)
    {
        if (body == null) return null;
        var prop = body.GetType().GetProperty("message");
        return prop?.GetValue(body) as string;
    }

    private static int? ExtractStatus(IActionResult result)
    {
        return result switch
        {
            ObjectResult or => or.StatusCode,
            StatusCodeResult sr => sr.StatusCode,
            _ => null
        };
    }


    [Fact]
    public async Task GroupCreate_AlreadyInGroup_HasJsonMessage()
    {
        var ctx = GetDbContext();
        var ctrl = new GroupController(ctx, new TestShortCodeGenerator());
        var userId = Guid.NewGuid();
        var groupId = Guid.NewGuid();

        ctx.Users.Add(new User { Id = userId, Username = "u", GroupId = groupId });
        ctx.Groups.Add(new Group { Id = groupId, Name = "G", ShortCode = "X", OwnerId = Guid.NewGuid() });
        await ctx.SaveChangesAsync();
        SetUser(ctrl, userId);

        var result = await ctrl.CreateGroup(new CreateGroupRequest { Name = "X", YearsOfStudy = 1 });

        var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        ExtractMessage(bad.Value).Should().Contain("вже перебуваєте");
    }

    [Fact]
    public async Task GroupKick_NotOwner_Returns403WithMessage()
    {
        var ctx = GetDbContext();
        var ctrl = new GroupController(ctx, new TestShortCodeGenerator());
        var userId = Guid.NewGuid();
        SetUser(ctrl, userId);

        var result = await ctrl.Kick(Guid.NewGuid());

        ExtractStatus(result).Should().Be(403);
        var obj = result.Should().BeAssignableTo<ObjectResult>().Subject;
        ExtractMessage(obj.Value).Should().Contain("староста");
    }

    [Fact]
    public async Task GroupMine_NoGroup_Returns404WithMessage()
    {
        var ctx = GetDbContext();
        var ctrl = new GroupController(ctx, new TestShortCodeGenerator());
        var userId = Guid.NewGuid();
        ctx.Users.Add(new User { Id = userId, Username = "u" });
        await ctx.SaveChangesAsync();
        SetUser(ctrl, userId);

        var result = await ctrl.GetMine();

        var nf = result.Should().BeOfType<NotFoundObjectResult>().Subject;
        ExtractMessage(nf.Value).Should().NotBeNullOrEmpty();
    }


    [Fact]
    public async Task InvitationSend_NotOwner_Returns403WithMessage()
    {
        var ctx = GetDbContext();
        var ctrl = new InvitationController(ctx, new FakeNotificationService());
        var userId = Guid.NewGuid();
        ctx.Users.Add(new User { Id = userId, Username = "u" });
        await ctx.SaveChangesAsync();
        SetUser(ctrl, userId);

        var result = await ctrl.Send(new SendInvitationRequest { UsernameOrEmail = "x" });

        ExtractStatus(result).Should().Be(403);
        ExtractMessage((result as ObjectResult)?.Value).Should().Contain("староста");
    }

    [Fact]
    public async Task InvitationAccept_NotFound_HasJsonMessage()
    {
        var ctx = GetDbContext();
        var ctrl = new InvitationController(ctx, new FakeNotificationService());
        var userId = Guid.NewGuid();
        SetUser(ctrl, userId);

        var result = await ctrl.Accept(Guid.NewGuid());

        var nf = result.Should().BeOfType<NotFoundObjectResult>().Subject;
        ExtractMessage(nf.Value).Should().NotBeNullOrEmpty();
    }


    [Fact]
    public async Task ScheduleCreate_NotOwner_Returns403WithMessage()
    {
        var ctx = GetDbContext();
        var ctrl = new ScheduleController(ctx, new FakeScheduleParser());
        SetUser(ctrl, Guid.NewGuid());

        var dto = new ScheduleEntryWriteDto("X", ScheduleEntryType.Lecture, DayOfWeek.Monday,
            new TimeOnly(8, 0), new TimeOnly(9, 0), null, null);
        var result = await ctrl.Create(dto);

        ExtractStatus(result).Should().Be(403);
        ExtractMessage((result as ObjectResult)?.Value).Should().Contain("староста");
    }

    [Fact]
    public async Task ScheduleCreate_BadTimes_HasJsonMessage()
    {
        var ctx = GetDbContext();
        var ctrl = new ScheduleController(ctx, new FakeScheduleParser());
        var ownerId = Guid.NewGuid();
        ctx.Groups.Add(new Group { Id = Guid.NewGuid(), Name = "G", ShortCode = "X", OwnerId = ownerId });
        await ctx.SaveChangesAsync();
        SetUser(ctrl, ownerId);

        var dto = new ScheduleEntryWriteDto("X", ScheduleEntryType.Lecture, DayOfWeek.Monday,
            new TimeOnly(12, 0), new TimeOnly(10, 0), null, null);
        var result = await ctrl.Create(dto);

        var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        ExtractMessage(bad.Value).Should().Contain("Час");
    }


    [Fact]
    public async Task StorageUploadNoFile_HasJsonMessage()
    {
        var ctx = GetDbContext();
        var ctrl = new StorageController(ctx, new FakeR2StorageService(), new FakeNotificationService());
        SetUser(ctrl, Guid.NewGuid());

        var result = await ctrl.Upload(null!);

        var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        ExtractMessage(bad.Value).Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task StorageDelete_NotUploader_Returns403WithMessage()
    {
        var ctx = GetDbContext();
        var ctrl = new StorageController(ctx, new FakeR2StorageService(), new FakeNotificationService());
        var stranger = Guid.NewGuid();
        var matId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        ctx.Groups.Add(new Group { Id = groupId, Name = "G", ShortCode = "X", OwnerId = Guid.NewGuid() });
        ctx.LectureMaterials.Add(new LectureMaterial
        {
            Id = matId, GroupId = groupId, UploaderId = Guid.NewGuid(),
            FileName = "x.pdf", ContentType = "application/pdf", R2Key = "k"
        });
        await ctx.SaveChangesAsync();
        SetUser(ctrl, stranger);

        var result = await ctrl.DeleteMaterial(matId);

        ExtractStatus(result).Should().Be(403);
        ExtractMessage((result as ObjectResult)?.Value).Should().NotBeNullOrEmpty();
    }


    [Fact]
    public async Task PlannerCreate_NotOwner_Returns403WithMessage()
    {
        var ctx = GetDbContext();
        var ctrl = new PlannerController(ctx);
        SetUser(ctrl, Guid.NewGuid());

        var result = await ctrl.CreateAssignment(new AssignmentDto("X", "y", DateTime.UtcNow.AddDays(7)));

        ExtractStatus(result).Should().Be(403);
        ExtractMessage((result as ObjectResult)?.Value).Should().Contain("староста");
    }


    [Fact]
    public async Task DigestGenerate_NotOwner_Returns403WithMessage()
    {
        var ctx = GetDbContext();
        var ctrl = new DigestController(ctx, new FakeDigestAIClient(), new FakeNotificationService());
        SetUser(ctrl, Guid.NewGuid());

        var result = await ctrl.Generate("day");

        ExtractStatus(result).Should().Be(403);
        ExtractMessage((result as ObjectResult)?.Value).Should().Contain("староста");
    }

    [Fact]
    public async Task DigestGenerate_InvalidPeriod_HasJsonMessage()
    {
        var ctx = GetDbContext();
        var ctrl = new DigestController(ctx, new FakeDigestAIClient(), new FakeNotificationService());
        SetUser(ctrl, Guid.NewGuid());

        var result = await ctrl.Generate("week");

        var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        ExtractMessage(bad.Value).Should().Contain("period");
    }


    [Fact]
    public async Task ScheduleCreate_ReturnsDto_NotEntity()
    {
        var ctx = GetDbContext();
        var ctrl = new ScheduleController(ctx, new FakeScheduleParser());
        var ownerId = Guid.NewGuid();
        ctx.Groups.Add(new Group { Id = Guid.NewGuid(), Name = "G", ShortCode = "X", OwnerId = ownerId });
        await ctx.SaveChangesAsync();
        SetUser(ctrl, ownerId);

        var dto = new ScheduleEntryWriteDto("Алгоритми", ScheduleEntryType.Lecture, DayOfWeek.Monday,
            new TimeOnly(8, 30), new TimeOnly(10, 0), "304", "Іваненко");
        var result = await ctrl.Create(dto);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeOfType<ScheduleEntryDto>();
    }

    [Fact]
    public async Task PlannerCreate_ReturnsDto_NotEntity()
    {
        var ctx = GetDbContext();
        var ctrl = new PlannerController(ctx);
        var ownerId = Guid.NewGuid();
        ctx.Groups.Add(new Group { Id = Guid.NewGuid(), Name = "G", ShortCode = "X", OwnerId = ownerId });
        await ctx.SaveChangesAsync();
        SetUser(ctrl, ownerId);

        var result = await ctrl.CreateAssignment(new AssignmentDto("Лаба", "опис", DateTime.UtcNow.AddDays(7)));

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeOfType<AssignmentResponseDto>();
    }

    [Fact]
    public async Task NotificationMarkRead_OthersNotif_Returns404WithMessage()
    {
        var ctx = GetDbContext();
        var ctrl = new NotificationController(ctx);
        var meId = Guid.NewGuid();
        var others = Guid.NewGuid();
        var notifId = Guid.NewGuid();
        ctx.Notifications.Add(new Notification
        {
            Id = notifId, UserId = others, Type = NotificationType.MentionInChat,
            Title = "x", Body = "y"
        });
        await ctx.SaveChangesAsync();
        SetUser(ctrl, meId);

        var result = await ctrl.MarkRead(notifId);

        var nf = result.Should().BeOfType<NotFoundObjectResult>().Subject;
        ExtractMessage(nf.Value).Should().NotBeNullOrEmpty();
    }
}
