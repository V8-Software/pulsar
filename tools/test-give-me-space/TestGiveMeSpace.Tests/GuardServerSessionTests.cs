using System.Windows.Threading;
using TestGiveMeSpace.Core;
using TestGiveMeSpace.Server;

namespace TestGiveMeSpace.Tests;

public sealed class GuardServerSessionTests
{
    [Fact]
    public async Task Visibility_commands_require_owner_and_keep_running_session()
    {
        var window = new FakePlaqueWindowGroup();
        var state = new GuardStateMachine();
        state.Request("chat-1");
        state.CompleteCountdown();
        GuardServerSession session = new(
            Dispatcher.CurrentDispatcher,
            window,
            new StateStore(TempFilePath()),
            state);

        GuardResponse rejected = await session.HandleAsync(new GuardRequest(GuardCommand.Hide, GuardPurpose.Test, "chat-2"), CancellationToken.None);
        GuardResponse hidden = await session.HandleAsync(new GuardRequest(GuardCommand.Hide, GuardPurpose.Test, "chat-1"), CancellationToken.None);
        GuardResponse shown = await session.HandleAsync(new GuardRequest(GuardCommand.Show, GuardPurpose.Test, "chat-1"), CancellationToken.None);

        Assert.Equal(GuardStatus.OwnerMismatch, rejected.Status);
        Assert.Equal(GuardStatus.Granted, hidden.Status);
        Assert.Equal(GuardStatus.Granted, shown.Status);
        Assert.Equal(1, window.HidePlaqueCallCount);
        Assert.Equal(1, window.ShowPlaqueCallCount);
        Assert.Equal(GuardStatus.Running, state.State);
    }

    [Fact]
    public async Task Relocation_commands_require_the_owner_of_the_running_session()
    {
        GuardStateMachine stateMachine = new();
        stateMachine.Request("chat-1");
        stateMachine.CompleteCountdown();
        FakePlaqueWindowGroup plaque = new();
        GuardServerSession session = new(
            Dispatcher.CurrentDispatcher,
            plaque,
            new StateStore(TempFilePath()),
            stateMachine);

        GuardResponse withoutOwner = await session.HandleAsync(
            new GuardRequest(GuardCommand.AvoidPoint, GuardPurpose.Test, Owner: null, X: -120, Y: 340),
            CancellationToken.None);
        GuardResponse foreignOwner = await session.HandleAsync(
            new GuardRequest(GuardCommand.AvoidPoint, GuardPurpose.Test, "chat-2", X: -120, Y: 340),
            CancellationToken.None);
        GuardResponse owner = await session.HandleAsync(
            new GuardRequest(GuardCommand.AvoidPoint, GuardPurpose.Test, "chat-1", X: -120, Y: 340),
            CancellationToken.None);
        GuardResponse restore = await session.HandleAsync(
            new GuardRequest(GuardCommand.RestorePosition, GuardPurpose.Test, "chat-1"),
            CancellationToken.None);

        Assert.Equal(GuardStatus.OwnerMismatch, withoutOwner.Status);
        Assert.Equal(GuardStatus.OwnerMismatch, foreignOwner.Status);
        Assert.Equal(GuardStatus.Granted, owner.Status);
        Assert.Equal(GuardStatus.Granted, restore.Status);
        Assert.Equal(1, plaque.AvoidPointCallCount);
        Assert.Equal((-120, 340), plaque.LastAvoidedPoint);
        Assert.Equal(1, plaque.RestorePositionsCallCount);
        Assert.Equal(GuardStatus.Running, stateMachine.State);
    }

    private static string TempFilePath()
        => Path.Combine(Path.GetTempPath(), "TestGiveMeSpace.Tests", $"{Guid.NewGuid():N}.json");

    private sealed class FakePlaqueWindowGroup : IPlaqueWindowGroup
    {
        public event EventHandler? LeftClickRequested
        {
            add { }
            remove { }
        }

        public event EventHandler? CloseRequested
        {
            add { }
            remove { }
        }

        public int AvoidPointCallCount { get; private set; }

        public int RestorePositionsCallCount { get; private set; }

        public int ShowPlaqueCallCount { get; private set; }

        public int HidePlaqueCallCount { get; private set; }

        public (int X, int Y) LastAvoidedPoint { get; private set; }

        public void SetDisplayText(string text)
        {
        }

        public void ShowPlaque()
        {
            ShowPlaqueCallCount++;
        }

        public void HidePlaque()
        {
            HidePlaqueCallCount++;
        }

        public bool AvoidPoint(int x, int y)
        {
            AvoidPointCallCount++;
            LastAvoidedPoint = (x, y);
            return true;
        }

        public bool RestorePositions()
        {
            RestorePositionsCallCount++;
            return true;
        }
    }
}
