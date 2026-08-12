namespace TestGiveMeSpace.Server;

internal interface IPlaqueWindow
{
    event EventHandler? LeftClickRequested;

    event EventHandler? CloseRequested;

    IntPtr MonitorHandle { get; }

    void SetDisplayText(string text);

    void ShowPlaque();

    void HidePlaque();

    bool TryGetBounds(out int left, out int top, out int width, out int height);

    bool TryMoveTo(int left, int top);
}
