using TestGiveMeSpace.Core;

namespace TestGiveMeSpace.Tests;

public sealed class StateStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "tgms-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Missing_state_file_returns_idle()
    {
        var store = new StateStore(_dir);

        var state = store.ReadTerminalState();

        Assert.Equal(GuardStatus.Idle, state);
    }

    [Fact]
    public void Corrupted_state_file_returns_idle()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "state.json"), "{not json");
        var store = new StateStore(_dir);

        var state = store.ReadTerminalState();

        Assert.Equal(GuardStatus.Idle, state);
    }

    [Fact]
    public void Writes_and_reads_stopped_by_user_terminal_state()
    {
        var store = new StateStore(_dir);

        store.WriteTerminalState(GuardStatus.StoppedByUser);

        Assert.Equal(GuardStatus.StoppedByUser, store.ReadTerminalState());
    }

    [Fact]
    public void Writes_and_reads_terminal_state_owner_and_start_time()
    {
        var store = new StateStore(_dir);
        DateTimeOffset startedAtUtc = new(2026, 5, 20, 0, 46, 18, TimeSpan.Zero);

        store.WriteTerminalState(
            GuardStatus.ClosedByUser,
            owner: "chat-1",
            startedAtUtc: startedAtUtc);

        GuardTerminalState state = store.ReadTerminalStateInfo();
        Assert.Equal(GuardStatus.ClosedByUser, state.Status);
        Assert.Equal("chat-1", state.Owner);
        Assert.Equal(startedAtUtc, state.StartedAtUtc);
    }

    [Fact]
    public void Writes_and_reads_timeout_terminal_state()
    {
        var store = new StateStore(_dir);
        DateTimeOffset startedAtUtc = new(2026, 6, 13, 12, 0, 0, TimeSpan.Zero);

        store.WriteTerminalState(
            GuardStatus.ClosedByTimeout,
            owner: "chat-1",
            startedAtUtc: startedAtUtc);

        GuardTerminalState state = store.ReadTerminalStateInfo();
        Assert.Equal(GuardStatus.ClosedByTimeout, state.Status);
        Assert.Equal("chat-1", state.Owner);
        Assert.Equal(startedAtUtc, state.StartedAtUtc);
    }

    [Fact]
    public void Cancelled_by_user_is_not_persisted_as_terminal_state()
    {
        var store = new StateStore(_dir);

        store.WriteTerminalState(GuardStatus.CancelledByUser);

        Assert.Equal(GuardStatus.Idle, store.ReadTerminalState());
    }

    [Fact]
    public void Clear_removes_terminal_state()
    {
        var store = new StateStore(_dir);
        store.WriteTerminalState(GuardStatus.ClosedByUser);

        store.Clear();

        Assert.Equal(GuardStatus.Idle, store.ReadTerminalState());
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }
}
