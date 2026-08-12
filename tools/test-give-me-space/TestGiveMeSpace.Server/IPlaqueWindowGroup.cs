namespace TestGiveMeSpace.Server;

internal interface IPlaqueWindowGroup
{
    event EventHandler? LeftClickRequested;

    event EventHandler? CloseRequested;

    void SetDisplayText(string text);

    void ShowPlaque();

    void HidePlaque();

    bool AvoidPoint(int x, int y);

    bool RestorePositions();
}
