using TestGiveMeSpace.Core;

namespace TestGiveMeSpace.Server;

internal sealed class PlaqueWindowGroup : IPlaqueWindowGroup
{
    private const int PlaqueMargin = 16;

    private readonly IPlaqueWindow primaryWindow;
    private readonly Func<IReadOnlyList<MonitorWorkingArea>> getMonitorWorkingAreas;
    private readonly Func<MonitorWorkingArea, IPlaqueWindow> createWindow;
    private readonly List<IPlaqueWindow> windows = [];
    private string displayText = string.Empty;
    private readonly Dictionary<IPlaqueWindow, WindowBounds> savedBounds = [];

    public PlaqueWindowGroup(SettingsStore settingsStore)
        : this(
            new MainWindow(settingsStore),
            NativeMethods.GetMonitorWorkingAreas,
            workingArea => new MainWindow(settingsStore, workingArea))
    {
    }

    internal PlaqueWindowGroup(
        IPlaqueWindow primaryWindow,
        Func<IReadOnlyList<MonitorWorkingArea>> getMonitorWorkingAreas,
        Func<MonitorWorkingArea, IPlaqueWindow> createWindow)
    {
        this.primaryWindow = primaryWindow;
        this.getMonitorWorkingAreas = getMonitorWorkingAreas;
        this.createWindow = createWindow;
        AddWindow(primaryWindow);
    }

    public event EventHandler? LeftClickRequested;

    public event EventHandler? CloseRequested;

    public void SetDisplayText(string text)
    {
        displayText = text;
        foreach (IPlaqueWindow window in windows)
        {
            window.SetDisplayText(text);
        }
    }

    public void ShowPlaque()
    {
        primaryWindow.ShowPlaque();
        CreateWindowsForOtherScreens();
        foreach (IPlaqueWindow window in windows.Skip(1))
        {
            window.ShowPlaque();
        }
    }

    public void HidePlaque()
    {
        RestorePositions();
        foreach (IPlaqueWindow window in windows)
        {
            window.HidePlaque();
        }
    }

    public bool AvoidPoint(int x, int y)
    {
        foreach (IPlaqueWindow window in windows)
        {
            if (!window.TryGetBounds(out int left, out int top, out int width, out int height))
            {
                continue;
            }

            if (!Intersects(left, top, width, height, x, y))
            {
                continue;
            }

            if (!TryRelocate(window, left, top, width, height, x, y, out int newLeft, out int newTop))
            {
                return false;
            }

            if (!window.TryMoveTo(newLeft, newTop))
            {
                return false;
            }

            SaveOriginalBounds(window, left, top);
            return true;
        }

        return true;
    }

    public bool RestorePositions()
    {
        bool restored = true;
        foreach ((IPlaqueWindow window, WindowBounds bounds) in savedBounds)
        {
            restored &= window.TryMoveTo(bounds.Left, bounds.Top);
        }

        if (restored)
        {
            savedBounds.Clear();
        }

        return restored;
    }

    private void CreateWindowsForOtherScreens()
    {
        if (windows.Count > 1)
        {
            return;
        }

        IntPtr primaryMonitor = primaryWindow.MonitorHandle;
        foreach (MonitorWorkingArea workingArea in getMonitorWorkingAreas())
        {
            if (workingArea.MonitorHandle == primaryMonitor)
            {
                continue;
            }

            IPlaqueWindow replica = createWindow(workingArea);
            replica.SetDisplayText(displayText);
            AddWindow(replica);
        }
    }

    private void AddWindow(IPlaqueWindow window)
    {
        window.LeftClickRequested += (_, _) => LeftClickRequested?.Invoke(this, EventArgs.Empty);
        window.CloseRequested += (_, _) => CloseRequested?.Invoke(this, EventArgs.Empty);
        windows.Add(window);
    }

    private static bool Intersects(int left, int top, int width, int height, int x, int y)
        => x >= left && x < left + width && y >= top && y < top + height;

    private bool TryRelocate(
        IPlaqueWindow window,
        int left,
        int top,
        int width,
        int height,
        int x,
        int y,
        out int newLeft,
        out int newTop)
    {
        newLeft = left;
        newTop = top;

        MonitorWorkingArea? matchingWorkingArea = getMonitorWorkingAreas()
            .Where(area => area.MonitorHandle == window.MonitorHandle)
            .Select(area => (MonitorWorkingArea?)area)
            .FirstOrDefault();
        if (!matchingWorkingArea.HasValue)
        {
            return false;
        }

        MonitorWorkingArea workingArea = matchingWorkingArea.Value;

        Span<int> leftCandidates =
        [
            left,
            workingArea.Left + PlaqueMargin,
            workingArea.Right - width - PlaqueMargin,
            workingArea.Left + PlaqueMargin,
        ];
        Span<int> topCandidates =
        [
            top,
            workingArea.Top + PlaqueMargin,
            workingArea.Top + PlaqueMargin,
            workingArea.Bottom - height - PlaqueMargin,
        ];

        int bestLeft = left;
        int bestTop = top;
        int bestRank = int.MaxValue;
        for (int i = 0; i < leftCandidates.Length; i++)
        {
            int candidateLeft = leftCandidates[i];
            int candidateTop = topCandidates[i];
            int clampedLeft = Math.Clamp(
                candidateLeft,
                workingArea.Left,
                Math.Max(workingArea.Left, workingArea.Right - width));
            int clampedTop = Math.Clamp(
                candidateTop,
                workingArea.Top,
                Math.Max(workingArea.Top, workingArea.Bottom - height));

            if (Intersects(clampedLeft, clampedTop, width, height, x, y))
            {
                continue;
            }

            int rank = DistanceRank(clampedLeft, clampedTop, width, height, x, y);
            if (rank < bestRank)
            {
                bestRank = rank;
                bestLeft = clampedLeft;
                bestTop = clampedTop;
            }
        }

        if (bestRank == int.MaxValue)
        {
            return false;
        }

        newLeft = bestLeft;
        newTop = bestTop;
        return true;
    }

    private static int DistanceRank(int left, int top, int width, int height, int x, int y)
    {
        long centerX = (long)left + width / 2;
        long centerY = (long)top + height / 2;
        long dx = centerX - x;
        long dy = centerY - y;
        return (int)Math.Min(dx * dx + dy * dy, int.MaxValue);
    }

    private void SaveOriginalBounds(IPlaqueWindow window, int left, int top)
    {
        if (!savedBounds.ContainsKey(window))
        {
            savedBounds[window] = new WindowBounds(left, top);
        }
    }

    private readonly record struct WindowBounds(int Left, int Top);
}
