using System.Threading;
using TestGiveMeSpace.Core;

namespace TestGiveMeSpace.Server;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length > 0 && args is not ["--server"] and not ["--server-debug"])
        {
            return ExitCodes.ProtocolError;
        }

        GuardAppPaths paths = GuardAppPaths.Create();
        using Mutex mutex = new(initiallyOwned: true, paths.MutexName, out bool createdNew);
        if (!createdNew)
        {
            return ExitCodes.Success;
        }

        App app = new();
        SettingsStore settingsStore = new(paths.DataDirectory);
        PlaqueWindowGroup window = new(settingsStore);
        StateStore stateStore = new(paths.StatePath);
        GuardServerSession session = new(app.Dispatcher, window, stateStore);
        GuardPipeServer pipeServer = new(paths.PipeName, session);
        using CancellationTokenSource serverCancellation = new();
        app.Exit += (_, _) => serverCancellation.Cancel();
        _ = pipeServer.RunAsync(serverCancellation.Token);
        return app.Run();
    }
}
