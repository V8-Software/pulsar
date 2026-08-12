namespace TestGiveMeSpace.Core;

public interface IGuardServerProcess
{
    bool IsRunning();

    void EnsureStarted();

    Task<bool> WaitUntilStoppedAsync(TimeSpan timeout, CancellationToken cancellationToken);
}
