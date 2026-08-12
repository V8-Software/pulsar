using System.Diagnostics;
using System.IO;

namespace TestGiveMeSpace.Core;

public sealed record GuardAppPaths(
    string PipeName,
    string MutexName,
    string DataDirectory,
    string StatePath)
{
    public static GuardAppPaths Create()
    {
        int sessionId = Process.GetCurrentProcess().SessionId;
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
        {
            localAppData = Path.GetTempPath();
        }

        string dataDirectory = Path.Combine(localAppData, "TestGiveMeSpace");
        return new GuardAppPaths(
            $"TestGiveMeSpace.{sessionId}",
            $@"Local\TestGiveMeSpace.{sessionId}",
            dataDirectory,
            Path.Combine(dataDirectory, "state.json"));
    }
}
