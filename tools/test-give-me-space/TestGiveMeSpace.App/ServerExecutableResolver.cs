using System.IO;

namespace TestGiveMeSpace.App;

internal static class ServerExecutableResolver
{
    private const string ServerExecutableName = "test-give-me-space-server.exe";
    private const string OverrideVariable = "TEST_GIVE_ME_SPACE_SERVER_EXE";

    public static string Resolve(string cliExecutablePath)
    {
        string? overridePath = Environment.GetEnvironmentVariable(OverrideVariable);
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            return RequireExistingFile(overridePath.Trim());
        }

        string? directory = Path.GetDirectoryName(cliExecutablePath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            directory = Environment.CurrentDirectory;
        }

        return RequireExistingFile(Path.Combine(directory, ServerExecutableName));
    }

    private static string RequireExistingFile(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"Сервер test-give-me-space не найден: {path}",
                path);
        }

        return path;
    }
}
