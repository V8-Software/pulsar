using System.IO;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using TestGiveMeSpace.Core;

namespace TestGiveMeSpace.Server;

public partial class MainWindow : Window, IPlaqueWindow
{
    private const double DragThreshold = 6.0;
    private static readonly TimeSpan CursorPollInterval = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan CursorHighlightDuration = TimeSpan.FromSeconds(1);
    private static readonly Brush NormalBackground = new SolidColorBrush(Color.FromRgb(0x11, 0x11, 0x11));
    private static readonly Brush NormalForeground = Brushes.White;
    private static readonly Brush NormalBorderBrush = NormalBackground;
    private static readonly Brush ActiveMouseBackground = Brushes.White;
    private static readonly Brush ActiveMouseForeground = Brushes.Black;
    private static readonly Brush ActiveMouseBorderBrush = Brushes.Black;

    private readonly SettingsStore settingsStore;
    private readonly MonitorWorkingArea? forcedWorkingArea;
    private Point? dragStartScreen;
    private Point? lastCursorPosition;
    private DispatcherTimer? cursorActivityTimer;
    private DateTimeOffset? cursorHighlightUntilUtc;
    private double dragStartLeft;
    private double dragStartTop;
    private bool isDragging;
    private bool noActivateStyleApplied;
    private bool cursorHighlightVisible;

    public MainWindow(SettingsStore settingsStore)
        : this(settingsStore, null)
    {
    }

    internal MainWindow(SettingsStore settingsStore, MonitorWorkingArea? forcedWorkingArea)
    {
        this.settingsStore = settingsStore;
        this.forcedWorkingArea = forcedWorkingArea;
        InitializeComponent();
        SourceInitialized += (_, _) => EnsureNoActivateStyle();
    }

    public event EventHandler? LeftClickRequested;

    public event EventHandler? CloseRequested;

    IntPtr IPlaqueWindow.MonitorHandle =>
        NativeMethods.GetWindowMonitor(new WindowInteropHelper(this).Handle);

    public void SetDisplayText(string text)
    {
        MessageTextBlock.Inlines.Clear();
        int countdownSeparatorIndex = text.LastIndexOf(": ", StringComparison.Ordinal);
        if (countdownSeparatorIndex >= 0
            && int.TryParse(text[(countdownSeparatorIndex + 2)..], out _))
        {
            MessageTextBlock.Inlines.Add(new Run(text[..(countdownSeparatorIndex + 2)]));
            MessageTextBlock.Inlines.Add(new Run(text[(countdownSeparatorIndex + 2)..])
            {
                FontWeight = FontWeights.Bold,
            });
        }
        else
        {
            MessageTextBlock.Inlines.Add(new Run(text));
        }

    }

    public void ShowPlaque()
    {
        Topmost = true;
        if (!IsVisible)
        {
            EnsureNoActivateStyle();
            if (forcedWorkingArea is null)
            {
                ApplySavedOrDefaultPosition();
            }

            Show();
            if (forcedWorkingArea is MonitorWorkingArea workingArea)
            {
                UpdateLayout();
                GuardSettings settings = settingsStore.Load();
                GuardPosition? savedPosition = null;
                settings.MonitorPositions?.TryGetValue(workingArea.DeviceName, out savedPosition);
                NativeMethods.PositionAtTopRight(
                    new WindowInteropHelper(this).Handle,
                    workingArea,
                    savedPosition);
            }
        }

        StartCursorActivityMonitor();
    }

    private void EnsureNoActivateStyle()
    {
        if (noActivateStyleApplied)
        {
            return;
        }

        IntPtr handle = new WindowInteropHelper(this).EnsureHandle();
        NativeMethods.ApplyNoActivateStyle(handle);
        noActivateStyleApplied = true;
    }

    public void HidePlaque()
    {
        CloseMenu.Visibility = Visibility.Collapsed;
        StopCursorActivityMonitor();
        Hide();
    }

    public bool TryGetBounds(out int left, out int top, out int width, out int height)
        => NativeMethods.TryGetWindowBounds(
            new WindowInteropHelper(this).Handle,
            out left,
            out top,
            out width,
            out height);

    public bool TryMoveTo(int left, int top)
        => NativeMethods.TryMoveWindow(new WindowInteropHelper(this).Handle, left, top);

    private void ApplySavedOrDefaultPosition()
    {
        GuardSettings settings = settingsStore.Load();
        Size desiredSize = MeasureDesiredSize();
        Rect workArea = SystemParameters.WorkArea;
        double left = settings.Left ?? workArea.Right - desiredSize.Width - 16;
        double top = settings.Top ?? workArea.Top + 16;

        Left = Clamp(left, workArea.Left, Math.Max(workArea.Left, workArea.Right - desiredSize.Width));
        Top = Clamp(top, workArea.Top, Math.Max(workArea.Top, workArea.Bottom - desiredSize.Height));
    }

    private Size MeasureDesiredSize()
    {
        Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return DesiredSize.Width > 0 && DesiredSize.Height > 0
            ? DesiredSize
            : new Size(260, 28);
    }

    private static double Clamp(double value, double minimum, double maximum)
        => Math.Min(Math.Max(value, minimum), maximum);

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (CloseMenu.Visibility == Visibility.Visible)
        {
            CloseMenu.Visibility = Visibility.Collapsed;
        }

        isDragging = false;
        dragStartScreen = ToDip(PointToScreen(e.GetPosition(this)));
        dragStartLeft = Left;
        dragStartTop = Top;
        CaptureMouse();
        e.Handled = true;
    }

    private void Window_MouseMove(object sender, MouseEventArgs e)
    {
        if (dragStartScreen is null || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        Point current = ToDip(PointToScreen(e.GetPosition(this)));
        Vector delta = current - dragStartScreen.Value;
        if (!isDragging && delta.Length < DragThreshold)
        {
            return;
        }

        isDragging = true;
        Left = dragStartLeft + delta.X;
        Top = dragStartTop + delta.Y;
        e.Handled = true;
    }

    private void Window_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (dragStartScreen is null)
        {
            return;
        }

        ReleaseMouseCapture();
        dragStartScreen = null;
        if (isDragging)
        {
            SavePosition();
        }
        else
        {
            LeftClickRequested?.Invoke(this, EventArgs.Empty);
        }

        isDragging = false;
        e.Handled = true;
    }

    private void Window_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        CloseMenu.Visibility = CloseMenu.Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;
        e.Handled = true;
    }

    private void StartCursorActivityMonitor()
    {
        lastCursorPosition = NativeMethods.GetCursorPosition();
        cursorHighlightUntilUtc = null;
        SetCursorHighlightVisible(false);
        cursorActivityTimer ??= new DispatcherTimer(
            CursorPollInterval,
            DispatcherPriority.Background,
            (_, _) => PollCursorActivity(),
            Dispatcher);
        cursorActivityTimer.Start();
    }

    private void StopCursorActivityMonitor()
    {
        cursorActivityTimer?.Stop();
        cursorHighlightUntilUtc = null;
        lastCursorPosition = null;
        SetCursorHighlightVisible(false);
    }

    private void PollCursorActivity()
    {
        Point current = NativeMethods.GetCursorPosition();
        if (!double.IsNaN(current.X)
            && lastCursorPosition is Point previous
            && (current - previous).Length > 0.5)
        {
            RegisterCursorActivity();
        }

        lastCursorPosition = current;
        if (cursorHighlightVisible
            && cursorHighlightUntilUtc is DateTimeOffset deadline
            && DateTimeOffset.UtcNow >= deadline)
        {
            cursorHighlightUntilUtc = null;
            SetCursorHighlightVisible(false);
        }
    }

    private void RegisterCursorActivity()
    {
        cursorHighlightUntilUtc = DateTimeOffset.UtcNow.Add(CursorHighlightDuration);
        SetCursorHighlightVisible(true);
    }

    private void SetCursorHighlightVisible(bool visible)
    {
        cursorHighlightVisible = visible;
        PlaqueBorder.Background = visible ? ActiveMouseBackground : NormalBackground;
        PlaqueBorder.BorderBrush = visible ? ActiveMouseBorderBrush : NormalBorderBrush;
        MessageTextBlock.Foreground = visible ? ActiveMouseForeground : NormalForeground;
        MouseIcon.Visibility = visible ? Visibility.Visible : Visibility.Hidden;
    }

    private void CloseMenu_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        CloseMenu.Visibility = Visibility.Collapsed;
        CloseRequested?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }

    private void CloseMenu_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        => e.Handled = true;

    private Point ToDip(Point screenPoint)
    {
        PresentationSource? source = PresentationSource.FromVisual(this);
        Matrix transform = source?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        return transform.Transform(screenPoint);
    }

    private void SavePosition()
    {
        if (forcedWorkingArea is not null)
        {
            MonitorWorkingArea workingArea = forcedWorkingArea.Value;
            if (NativeMethods.TryGetWindowPosition(
                new WindowInteropHelper(this).Handle,
                out int left,
                out int top))
            {
                try
                {
                    settingsStore.SaveMonitorPosition(workingArea.DeviceName, left, top);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }

            return;
        }

        try
        {
            settingsStore.SavePrimaryPosition(Left, Top);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
