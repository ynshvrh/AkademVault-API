using Xunit;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using AkademVault_API.Data;
using AkademVault_API.Hubs;
using AkademVault_API.Models;
using AkademVault_API.Services;
using System.Security.Claims;

namespace Tests;


// HubCallerContext stub: lets tests inject a ClaimsPrincipal and observe Abort().
internal class TestHubCallerContext : HubCallerContext
{
    public ClaimsPrincipal? UserPrincipal { get; init; }
    public string ConnId { get; init; } = "test-conn";
    public bool Aborted { get; private set; }

    public override string ConnectionId => ConnId;
    public override string? UserIdentifier => UserPrincipal?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    public override ClaimsPrincipal? User => UserPrincipal;
    public override IDictionary<object, object?> Items { get; } = new Dictionary<object, object?>();
    public override IFeatureCollection Features { get; } = new FeatureCollection();
    public override CancellationToken ConnectionAborted => default;
    public override void Abort() => Aborted = true;
}

// IClientProxy stub: records each SendCoreAsync call for assertion.
internal class TestClientProxy : IClientProxy
{
    public List<(string Method, object?[] Args)> Calls { get; } = new();

    public Task SendCoreAsync(string method, object?[] args, CancellationToken ct = default)
    {
        Calls.Add((method, args));
        return Task.CompletedTask;
    }
}

// IHubCallerClients stub that only implements Group(...) — every other accessor throws.
internal class TestHubCallerClients : IHubCallerClients
{
    public Dictionary<string, TestClientProxy> GroupCalls { get; } = new();

    public IClientProxy Group(string groupName)
    {
        if (!GroupCalls.TryGetValue(groupName, out var proxy))
        {
            proxy = new TestClientProxy();
            GroupCalls[groupName] = proxy;
        }
        return proxy;
    }

    public IClientProxy All => throw new NotImplementedException();
    public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => throw new NotImplementedException();
    public IClientProxy Client(string connectionId) => throw new NotImplementedException();
    public IClientProxy Clients(IReadOnlyList<string> connectionIds) => throw new NotImplementedException();
    public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => throw new NotImplementedException();
    public IClientProxy Groups(IReadOnlyList<string> groupNames) => throw new NotImplementedException();
    public IClientProxy User(string userId) => throw new NotImplementedException();
    public IClientProxy Users(IReadOnlyList<string> userIds) => throw new NotImplementedException();
    public IClientProxy Caller => throw new NotImplementedException();
    public IClientProxy Others => throw new NotImplementedException();
    public IClientProxy OthersInGroup(string groupName) => throw new NotImplementedException();
}

// IGroupManager stub that records every AddToGroup call.
internal class TestGroupManager : IGroupManager
{
    public List<(string ConnId, string Group)> Added { get; } = new();

    public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken ct = default)
    {
        Added.Add((connectionId, groupName));
        return Task.CompletedTask;
    }

    public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken ct = default)
        => Task.CompletedTask;
}

// Tests for ChatHub: SignalR group subscription, message validation, broadcast and @mention notifications.
public class ChatHubTests
{
    private AppDbContext GetDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    private static ChatHub BuildHub(AppDbContext ctx, FakeNotificationService notif, Guid userId,
        out TestHubCallerContext hubContext, out TestHubCallerClients clients, out TestGroupManager groups)
    {
        var hub = new ChatHub(ctx, notif);
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString())
        }, "test"));
        hubContext = new TestHubCallerContext { UserPrincipal = principal };
        clients = new TestHubCallerClients();
        groups = new TestGroupManager();
        hub.Context = hubContext;
        hub.Clients = clients;
        hub.Groups = groups;
        return hub;
    }

    // A user without a group is aborted instead of joining the SignalR room.
    [Fact]
    public async Task OnConnected_AbortsConnection_WhenUserHasNoGroup()
    {
        var ctx = GetDbContext();
        var userId = Guid.NewGuid();
        ctx.Users.Add(new User { Id = userId, Username = "alone" });
        await ctx.SaveChangesAsync();

        var hub = BuildHub(ctx, new FakeNotificationService(), userId,
            out var hubCtx, out var _, out var groups);

        await hub.OnConnectedAsync();

        hubCtx.Aborted.Should().BeTrue();
        groups.Added.Should().BeEmpty();
    }

    // The user with a group is subscribed to a SignalR group named after their GroupId.
    [Fact]
    public async Task OnConnected_SubscribesToGroupId_WhenUserHasGroup()
    {
        var ctx = GetDbContext();
        var userId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        ctx.Users.Add(new User { Id = userId, Username = "u", GroupId = groupId });
        await ctx.SaveChangesAsync();

        var hub = BuildHub(ctx, new FakeNotificationService(), userId,
            out var hubCtx, out _, out var groups);

        await hub.OnConnectedAsync();

        hubCtx.Aborted.Should().BeFalse();
        groups.Added.Should().ContainSingle()
            .Which.Should().Be((hubCtx.ConnId, groupId.ToString()));
    }

    // Empty/whitespace messages throw HubException so the SPA shows a validation toast.
    [Fact]
    public async Task SendMessage_ThrowsHubException_OnEmptyContent()
    {
        var ctx = GetDbContext();
        var userId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        ctx.Users.Add(new User { Id = userId, Username = "u", GroupId = groupId });
        await ctx.SaveChangesAsync();

        var hub = BuildHub(ctx, new FakeNotificationService(), userId, out _, out _, out _);

        await Assert.ThrowsAsync<HubException>(() => hub.SendMessage(""));
        await Assert.ThrowsAsync<HubException>(() => hub.SendMessage("   "));
    }

    // Messages longer than 2000 chars are rejected.
    [Fact]
    public async Task SendMessage_ThrowsHubException_OnTooLongContent()
    {
        var ctx = GetDbContext();
        var userId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        ctx.Users.Add(new User { Id = userId, Username = "u", GroupId = groupId });
        await ctx.SaveChangesAsync();

        var hub = BuildHub(ctx, new FakeNotificationService(), userId, out _, out _, out _);

        var tooLong = new string('a', 2001);
        await Assert.ThrowsAsync<HubException>(() => hub.SendMessage(tooLong));
    }

    // A user without a group cannot send anything.
    [Fact]
    public async Task SendMessage_ThrowsHubException_WhenUserHasNoGroup()
    {
        var ctx = GetDbContext();
        var userId = Guid.NewGuid();
        ctx.Users.Add(new User { Id = userId, Username = "u" });
        await ctx.SaveChangesAsync();

        var hub = BuildHub(ctx, new FakeNotificationService(), userId, out _, out _, out _);

        await Assert.ThrowsAsync<HubException>(() => hub.SendMessage("привіт"));
    }

    // Happy path: message is saved to DB and a ReceiveMessage event lands on the group's SignalR room.
    [Fact]
    public async Task SendMessage_PersistsAndBroadcasts_WhenValid()
    {
        var ctx = GetDbContext();
        var userId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        ctx.Users.Add(new User { Id = userId, Username = "alice", GroupId = groupId });
        await ctx.SaveChangesAsync();

        var hub = BuildHub(ctx, new FakeNotificationService(), userId,
            out _, out var clients, out _);

        await hub.SendMessage("Привіт групі!");

        (await ctx.ChatMessages.CountAsync()).Should().Be(1);
        var msg = await ctx.ChatMessages.FirstAsync();
        msg.Content.Should().Be("Привіт групі!");
        msg.GroupId.Should().Be(groupId);

        clients.GroupCalls.Should().ContainKey(groupId.ToString());
        clients.GroupCalls[groupId.ToString()].Calls.Should().ContainSingle()
            .Which.Method.Should().Be("ReceiveMessage");
    }

    // @mentions trigger notifications only for users in the same group (others are skipped).
    [Fact]
    public async Task SendMessage_TriggersMentionNotifications_ForGroupMembers()
    {
        var ctx = GetDbContext();
        var senderId = Guid.NewGuid();
        var mentionedId = Guid.NewGuid();
        var unrelatedId = Guid.NewGuid();
        var groupId = Guid.NewGuid();

        ctx.Users.Add(new User { Id = senderId, Username = "alice", GroupId = groupId });
        ctx.Users.Add(new User { Id = mentionedId, Username = "bob", GroupId = groupId });
        ctx.Users.Add(new User { Id = unrelatedId, Username = "carl", GroupId = Guid.NewGuid() });
        await ctx.SaveChangesAsync();

        var notif = new FakeNotificationService();
        var hub = BuildHub(ctx, notif, senderId, out _, out _, out _);

        await hub.SendMessage("Гей @bob і @carl, як справи?");

        notif.Sent.Should().ContainSingle()
            .Which.Type.Should().Be(NotificationType.MentionInChat);
        notif.Sent.Single().UserId.Should().Be(mentionedId, "carl з іншої групи — пропускаємо");
    }

    // @-mentioning yourself does not push a notification to your own inbox.
    [Fact]
    public async Task SendMessage_DoesNotNotifySelf_OnSelfMention()
    {
        var ctx = GetDbContext();
        var senderId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        ctx.Users.Add(new User { Id = senderId, Username = "alice", GroupId = groupId });
        await ctx.SaveChangesAsync();

        var notif = new FakeNotificationService();
        var hub = BuildHub(ctx, notif, senderId, out _, out _, out _);

        await hub.SendMessage("Я тут @alice");

        notif.Sent.Should().BeEmpty();
    }
}

// Tests for NotificationHub: aborts anonymous connections, subscribes authenticated ones to "user:{id}".
public class NotificationHubTests
{
    // A connection without a NameIdentifier claim is aborted before being added to any group.
    [Fact]
    public async Task OnConnected_AbortsWhenNoUserId()
    {
        var hub = new NotificationHub();
        var hubCtx = new TestHubCallerContext
        {
            UserPrincipal = new ClaimsPrincipal(new ClaimsIdentity())
        };
        var groups = new TestGroupManager();
        hub.Context = hubCtx;
        hub.Groups = groups;

        await hub.OnConnectedAsync();

        hubCtx.Aborted.Should().BeTrue();
        groups.Added.Should().BeEmpty();
    }

    // Authenticated user is added to SignalR group "user:{userId}" so push notifications can target them.
    [Fact]
    public async Task OnConnected_SubscribesToUserGroup()
    {
        var userId = Guid.NewGuid();
        var hub = new NotificationHub();
        var hubCtx = new TestHubCallerContext
        {
            UserPrincipal = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, userId.ToString())
            }, "test"))
        };
        var groups = new TestGroupManager();
        hub.Context = hubCtx;
        hub.Groups = groups;

        await hub.OnConnectedAsync();

        hubCtx.Aborted.Should().BeFalse();
        groups.Added.Should().ContainSingle()
            .Which.Should().Be((hubCtx.ConnId, $"user:{userId}"));
    }
}
