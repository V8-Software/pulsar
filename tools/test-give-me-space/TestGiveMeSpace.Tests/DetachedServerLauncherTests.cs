using System.Reflection;
using System.Runtime.InteropServices;
using TestGiveMeSpace.App;

namespace TestGiveMeSpace.Tests;

public sealed class DetachedServerLauncherTests
{
    [Fact]
    public void Server_resolver_uses_sibling_server_executable()
    {
        string directory = CreateTempDirectory();
        string cliPath = Path.Combine(directory, "test-give-me-space.exe");
        string serverPath = Path.Combine(directory, "test-give-me-space-server.exe");
        File.WriteAllText(cliPath, "");
        File.WriteAllText(serverPath, "");

        string resolved = InvokeResolveServerExecutable(cliPath);

        Assert.Equal(serverPath, resolved);
    }

    [Fact]
    public void Server_resolver_uses_override_environment_variable()
    {
        string directory = CreateTempDirectory();
        string cliPath = Path.Combine(directory, "test-give-me-space.exe");
        string serverPath = Path.Combine(directory, "custom-server.exe");
        File.WriteAllText(cliPath, "");
        File.WriteAllText(serverPath, "");
        string? oldOverride = Environment.GetEnvironmentVariable("TEST_GIVE_ME_SPACE_SERVER_EXE");

        try
        {
            Environment.SetEnvironmentVariable("TEST_GIVE_ME_SPACE_SERVER_EXE", serverPath);

            string resolved = InvokeResolveServerExecutable(cliPath);

            Assert.Equal(serverPath, resolved);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TEST_GIVE_ME_SPACE_SERVER_EXE", oldOverride);
        }
    }

    [Fact]
    public void Server_resolver_rejects_missing_server_executable()
    {
        string directory = CreateTempDirectory();
        string cliPath = Path.Combine(directory, "test-give-me-space.exe");
        File.WriteAllText(cliPath, "");

        TargetInvocationException exception = Assert.Throws<TargetInvocationException>(
            () => InvokeResolveServerExecutable(cliPath));

        Assert.IsType<FileNotFoundException>(exception.InnerException);
    }

    [Fact]
    public void Server_shortcut_targets_server_executable_without_arguments_or_minimized_style()
    {
        string directory = CreateTempDirectory();
        string shortcutPath = Path.Combine(directory, "test-give-me-space-server-test.lnk");
        string serverPath = Path.Combine(directory, "test-give-me-space-server.exe");
        File.WriteAllText(serverPath, "");

        InvokeCreateServerShortcut(shortcutPath, serverPath);

        Assert.Equal(serverPath, ReadShortcutProperty<string>(shortcutPath, "TargetPath"));
        Assert.Equal(string.Empty, ReadShortcutProperty<string>(shortcutPath, "Arguments"));
        Assert.NotEqual(7, ReadShortcutProperty<int>(shortcutPath, "WindowStyle"));
    }

    [Fact]
    public void Delete_expired_shortcuts_removes_only_old_test_give_me_space_links()
    {
        string directory = CreateTempDirectory();
        string oldShortcut = Path.Combine(directory, "test-give-me-space-server-123-old.lnk");
        string freshShortcut = Path.Combine(directory, "test-give-me-space-server-123-fresh.lnk");
        string otherShortcut = Path.Combine(directory, "other-tool.lnk");
        File.WriteAllText(oldShortcut, "old");
        File.WriteAllText(freshShortcut, "fresh");
        File.WriteAllText(otherShortcut, "other");
        DateTimeOffset now = new(2026, 5, 20, 10, 0, 0, TimeSpan.Zero);
        File.SetLastWriteTimeUtc(oldShortcut, now.AddHours(-3).UtcDateTime);
        File.SetLastWriteTimeUtc(freshShortcut, now.AddMinutes(-10).UtcDateTime);
        File.SetLastWriteTimeUtc(otherShortcut, now.AddHours(-3).UtcDateTime);

        int deleted = InvokeDeleteExpiredShortcuts(
            directory,
            now,
            TimeSpan.FromHours(1));

        Assert.Equal(1, deleted);
        Assert.False(File.Exists(oldShortcut));
        Assert.True(File.Exists(freshShortcut));
        Assert.True(File.Exists(otherShortcut));
    }

    private static string CreateTempDirectory()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "TestGiveMeSpace.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static string InvokeResolveServerExecutable(string cliPath)
    {
        Type type = typeof(Program).Assembly.GetType("TestGiveMeSpace.App.ServerExecutableResolver")
            ?? throw new InvalidOperationException("ServerExecutableResolver type was not found");
        MethodInfo method = type.GetMethod(
            "Resolve",
            BindingFlags.Public | BindingFlags.Static)
            ?? throw new InvalidOperationException("Resolve method was not found");

        return (string)(method.Invoke(null, [cliPath])
            ?? throw new InvalidOperationException("Resolve returned null"));
    }

    private static void InvokeCreateServerShortcut(string shortcutPath, string serverExecutablePath)
    {
        Type type = typeof(Program).Assembly.GetType("TestGiveMeSpace.App.DetachedServerLauncher")
            ?? throw new InvalidOperationException("DetachedServerLauncher type was not found");
        MethodInfo method = type.GetMethod(
            "CreateServerShortcut",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("CreateServerShortcut method was not found");

        method.Invoke(null, [shortcutPath, serverExecutablePath]);
    }

    private static T ReadShortcutProperty<T>(string shortcutPath, string propertyName)
    {
        Type shellType = Type.GetTypeFromProgID("WScript.Shell")
            ?? throw new InvalidOperationException("Windows WScript.Shell COM object is not available");
        object? shell = Activator.CreateInstance(shellType);
        if (shell is null)
        {
            throw new InvalidOperationException("Windows WScript.Shell COM object could not be created");
        }

        object? shortcut = null;
        try
        {
            shortcut = shellType.InvokeMember(
                "CreateShortcut",
                BindingFlags.InvokeMethod,
                binder: null,
                target: shell,
                args: [shortcutPath]);
            if (shortcut is null)
            {
                throw new InvalidOperationException("Windows shortcut object could not be created");
            }

            return (T)shortcut.GetType().InvokeMember(
                propertyName,
                BindingFlags.GetProperty,
                binder: null,
                target: shortcut,
                args: [])!;
        }
        finally
        {
            if (shortcut is not null && Marshal.IsComObject(shortcut))
            {
                Marshal.FinalReleaseComObject(shortcut);
            }

            if (Marshal.IsComObject(shell))
            {
                Marshal.FinalReleaseComObject(shell);
            }
        }
    }

    private static int InvokeDeleteExpiredShortcuts(
        string directory,
        DateTimeOffset now,
        TimeSpan maxAge)
    {
        Type type = typeof(Program).Assembly.GetType("TestGiveMeSpace.App.DetachedServerLauncher")
            ?? throw new InvalidOperationException("DetachedServerLauncher type was not found");
        MethodInfo method = type.GetMethod(
            "DeleteExpiredShortcuts",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("DeleteExpiredShortcuts method was not found");

        return (int)(method.Invoke(null, [directory, now, maxAge]) ?? 0);
    }
}
