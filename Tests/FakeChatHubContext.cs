using Microsoft.AspNetCore.SignalR;
using AkademVault_API.Hubs;

namespace Tests;

// No-op IHubContext<ChatHub> for controller tests that don't exercise SignalR broadcasts.
internal class FakeChatHubContext : IHubContext<ChatHub>
{
    public IHubClients Clients { get; } = new FakeHubClients();
    public IGroupManager Groups { get; } = new FakeChatHubGroupManager();
}

// IHubClients stub that returns the same no-op proxy for every accessor.
internal class FakeHubClients : IHubClients
{
    private static readonly IClientProxy Noop = new FakeHubClientProxy();

    public IClientProxy All => Noop;
    public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => Noop;
    public IClientProxy Client(string connectionId) => Noop;
    public IClientProxy Clients(IReadOnlyList<string> connectionIds) => Noop;
    public IClientProxy Group(string groupName) => Noop;
    public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => Noop;
    public IClientProxy Groups(IReadOnlyList<string> groupNames) => Noop;
    public IClientProxy User(string userId) => Noop;
    public IClientProxy Users(IReadOnlyList<string> userIds) => Noop;
}

// IClientProxy stub that drops every SendCoreAsync call on the floor.
internal class FakeHubClientProxy : IClientProxy
{
    public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}

// IGroupManager stub used by FakeChatHubContext; ignores add/remove.
internal class FakeChatHubGroupManager : IGroupManager
{
    public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
