using System.Collections.Generic;
using System.Threading.Tasks;
using EmberTern.App.ViewModels;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// The single owner of "does the application hold anything uncommitted" (etap I7.5).
/// <para>
/// It exists because Data Import stopped sharing the console's transaction, turning a question with one answer
/// into a question with several. The alternative was a growing list of module names inside the close guard,
/// and the module nobody remembers to add is the one that loses somebody's data.
/// </para>
/// </summary>
public class PendingWorkRegistryTests
{
    private sealed class FakeWork : IPendingTransactionalWork
    {
        public FakeWork(string label, bool hasWork) { Label = label; HasWork = hasWork; }

        public string Label { get; }
        public bool HasWork { get; set; }
        public List<string> Actions { get; } = new();

        public string Describe() => Label;
        public Task CommitAsync() { Actions.Add("commit"); HasWork = false; return Task.CompletedTask; }
        public Task RollbackAsync() { Actions.Add("rollback"); HasWork = false; return Task.CompletedTask; }
    }

    [Fact]
    public void HasWork_IsTrueWhenAnySourceHasWork()
    {
        var registry = new PendingWorkRegistry();
        var console = new FakeWork("console", hasWork: false);
        var import = new FakeWork("import", hasWork: false);
        registry.Register(console);
        registry.Register(import);

        Assert.False(registry.HasWork);

        import.HasWork = true;
        Assert.True(registry.HasWork);
    }

    /// <summary>Only sources that actually hold something speak — a guard that lists an empty transaction
    /// teaches the user to ignore it.</summary>
    [Fact]
    public void Describe_ListsOnlyTheSourcesThatHoldSomething()
    {
        var registry = new PendingWorkRegistry();
        registry.Register(new FakeWork("console", hasWork: false));
        registry.Register(new FakeWork("import: 500 rows", hasWork: true));

        Assert.Equal(new[] { "import: 500 rows" }, registry.Describe());
    }

    /// <summary>⭐ The reason the registry exists: settling at disconnect/exit must reach EVERY source, because
    /// the user was shown every line and answered about all of them at once.</summary>
    [Fact]
    public async Task SettlingReachesEverySourceThatHasWork()
    {
        var registry = new PendingWorkRegistry();
        var console = new FakeWork("console", hasWork: true);
        var import = new FakeWork("import", hasWork: true);
        var idle = new FakeWork("idle", hasWork: false);
        registry.Register(console);
        registry.Register(import);
        registry.Register(idle);

        await registry.CommitAllAsync();

        Assert.Equal(new[] { "commit" }, console.Actions);
        Assert.Equal(new[] { "commit" }, import.Actions);
        Assert.Empty(idle.Actions);
        Assert.False(registry.HasWork);
    }

    /// <summary>A closed import tab stops being anybody's business — it can no longer hold anything, so it must
    /// no longer be asked.</summary>
    [Fact]
    public void UnregisteredSource_StopsCounting()
    {
        var registry = new PendingWorkRegistry();
        var import = new FakeWork("import", hasWork: true);
        registry.Register(import);
        Assert.True(registry.HasWork);

        registry.Unregister(import);
        Assert.False(registry.HasWork);
    }
}
