using TestGiveMeSpace.Server;

namespace TestGiveMeSpace.Tests;

public sealed class PlaqueWindowGroupTests
{
    [Fact]
    public void Avoid_point_moves_only_the_covering_plaque_on_its_own_monitor_and_hide_restores_it()
    {
        FakePlaqueWindow primary = new((IntPtr)1, left: -100, top: 100, width: 200, height: 80);
        MonitorWorkingArea[] monitors =
        [
            new((IntPtr)1, -1920, 0, 0, 1080, @"\\\\.\\DISPLAY1"),
            new((IntPtr)2, 0, 0, 1920, 1080, @"\\\\.\\DISPLAY2"),
        ];
        FakePlaqueWindow secondary = new((IntPtr)2, left: 400, top: 100, width: 200, height: 80);
        PlaqueWindowGroup group = new(primary, () => monitors, _ => secondary);

        group.ShowPlaque();

        Assert.True(group.AvoidPoint(x: 50, y: 120));
        Assert.InRange(primary.Position.Left, -1920, -200);
        Assert.InRange(primary.Position.Top, 0, 1000);
        Assert.True(
            primary.Position.Left + 200 <= 50 ||
            primary.Position.Left > 50 ||
            primary.Position.Top + 80 <= 120 ||
            primary.Position.Top > 120);
        Assert.Equal((400, 100), secondary.Position);
        Assert.Equal(1, primary.MoveCount);
        Assert.Equal(0, secondary.MoveCount);

        group.HidePlaque();

        Assert.Equal((-100, 100), primary.Position);
        Assert.Equal(2, primary.MoveCount);

        Assert.True(group.RestorePositions());

        Assert.Equal(2, primary.MoveCount);
    }

    [Fact]
    public void Hide_keeps_original_position_available_when_restore_temporarily_fails()
    {
        FakePlaqueWindow primary = new((IntPtr)1, left: 100, top: 100, width: 200, height: 80);
        MonitorWorkingArea[] monitors =
        [
            new((IntPtr)1, 0, 0, 1920, 1080, @"\\.\DISPLAY1"),
        ];
        PlaqueWindowGroup group = new(primary, () => monitors, _ => throw new InvalidOperationException());

        group.ShowPlaque();
        Assert.True(group.AvoidPoint(x: 150, y: 120));
        Assert.NotEqual((100, 100), primary.Position);

        primary.FailNextMove = true;
        group.HidePlaque();

        Assert.NotEqual((100, 100), primary.Position);
        Assert.True(group.RestorePositions());
        Assert.Equal((100, 100), primary.Position);
        Assert.Equal(3, primary.MoveAttemptCount);
    }

    [Fact]
    public void Creates_one_replica_per_secondary_monitor_and_routes_all_events()
    {
        FakePlaqueWindow primary = new((IntPtr)1);
        MonitorWorkingArea[] monitors =
        [
            new((IntPtr)1, 0, 0, 1920, 1080, @"\\.\DISPLAY1"),
            new((IntPtr)2, 1920, 0, 3840, 1080, @"\\.\DISPLAY2"),
            new((IntPtr)3, -1920, 0, 0, 1080, @"\\.\DISPLAY3"),
        ];
        List<FakePlaqueWindow> replicas = [];
        PlaqueWindowGroup group = new(
            primary,
            () => monitors,
            workingArea =>
            {
                FakePlaqueWindow replica = new(workingArea.MonitorHandle);
                replicas.Add(replica);
                return replica;
            });
        int leftClicks = 0;
        int closeRequests = 0;
        group.LeftClickRequested += (_, _) => leftClicks++;
        group.CloseRequested += (_, _) => closeRequests++;

        group.SetDisplayText("Идёт тестирование");
        group.ShowPlaque();
        group.ShowPlaque();
        foreach (FakePlaqueWindow replica in replicas)
        {
            replica.RaiseLeftClickRequested();
            replica.RaiseCloseRequested();
        }
        group.HidePlaque();

        Assert.Equal(2, replicas.Count);
        Assert.All(replicas, replica => Assert.Equal("Идёт тестирование", replica.DisplayText));
        Assert.Equal(2, primary.ShowCount);
        Assert.All(replicas, replica => Assert.Equal(2, replica.ShowCount));
        Assert.Equal(1, primary.HideCount);
        Assert.All(replicas, replica => Assert.Equal(1, replica.HideCount));
        Assert.Equal(2, leftClicks);
        Assert.Equal(2, closeRequests);
    }

    private sealed class FakePlaqueWindow(
        IntPtr monitorHandle,
        int left = 0,
        int top = 0,
        int width = 100,
        int height = 40) : IPlaqueWindow
    {
        private int currentLeft = left;
        private int currentTop = top;

        public event EventHandler? LeftClickRequested;

        public event EventHandler? CloseRequested;

        public IntPtr MonitorHandle { get; } = monitorHandle;

        public string? DisplayText { get; private set; }

        public int ShowCount { get; private set; }

        public int HideCount { get; private set; }

        public int MoveCount { get; private set; }

        public int MoveAttemptCount { get; private set; }

        public bool FailNextMove { get; set; }

        public (int Left, int Top) Position => (currentLeft, currentTop);

        public void SetDisplayText(string text) => DisplayText = text;

        public void ShowPlaque() => ShowCount++;

        public void HidePlaque() => HideCount++;

        public bool TryGetBounds(out int currentLeft, out int currentTop, out int currentWidth, out int currentHeight)
        {
            currentLeft = this.currentLeft;
            currentTop = this.currentTop;
            currentWidth = width;
            currentHeight = height;
            return true;
        }

        public bool TryMoveTo(int newLeft, int newTop)
        {
            MoveAttemptCount++;
            if (FailNextMove)
            {
                FailNextMove = false;
                return false;
            }

            currentLeft = newLeft;
            currentTop = newTop;
            MoveCount++;
            return true;
        }

        public void RaiseLeftClickRequested() => LeftClickRequested?.Invoke(this, EventArgs.Empty);

        public void RaiseCloseRequested() => CloseRequested?.Invoke(this, EventArgs.Empty);
    }
}
