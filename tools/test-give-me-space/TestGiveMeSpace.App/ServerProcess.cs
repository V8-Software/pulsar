using TestGiveMeSpace.Core;

namespace TestGiveMeSpace.App;

internal sealed class ServerProcess(string executablePath) : IGuardServerProcess
{
    private static readonly TimeSpan ShutdownPollDelay = TimeSpan.FromMilliseconds(50);
    private readonly string mutexName = GuardAppPaths.Create().MutexName;

    public bool IsRunning()
    {
        try
        {
            using Mutex existing = Mutex.OpenExisting(mutexName);
            return true;
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
    }

    public void EnsureStarted()
    {
        string serverExecutablePath = ServerExecutableResolver.Resolve(executablePath);
        DetachedServerLauncher.Start(serverExecutablePath);
    }

    public async Task<bool> WaitUntilStoppedAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow.Add(timeout);
        while (IsRunning())
        {
            TimeSpan remaining = deadline - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                return false;
            }

            await Task.Delay(Min(ShutdownPollDelay, remaining), cancellationToken);
        }

        return true;
    }

    private static TimeSpan Min(TimeSpan first, TimeSpan second)
        => first <= second ? first : second;
}
