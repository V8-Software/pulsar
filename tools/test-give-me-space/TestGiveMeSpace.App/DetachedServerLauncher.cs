using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace TestGiveMeSpace.App;

internal static class DetachedServerLauncher
{
    private const string ShortcutPattern = "test-give-me-space-server-*.lnk";
    private static readonly TimeSpan ExplorerLaunchTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ShortcutMaxAge = TimeSpan.FromHours(1);

    public static void Start(string serverExecutablePath)
    {
        string tempDirectory = Path.GetTempPath();
        DeleteExpiredShortcuts(tempDirectory, DateTimeOffset.UtcNow, ShortcutMaxAge);
        string shortcutPath = Path.Combine(
            tempDirectory,
            $"test-give-me-space-server-{Environment.ProcessId}-{Guid.NewGuid():N}.lnk");
        CreateServerShortcut(shortcutPath, serverExecutablePath);

        bool keepShortcutForExplorer = false;
        try
        {
            // Opening a shortcut through Explorer prevents tree-waiting launchers
            // from treating the long-lived WPF server as a child of this request.
            ProcessStartInfo startInfo = new(ResolveExplorerPath())
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            startInfo.ArgumentList.Add(shortcutPath);

            using Process? explorer = Process.Start(startInfo);
            bool explorerExited = explorer?.WaitForExit(ExplorerLaunchTimeout) ?? true;
            keepShortcutForExplorer = !explorerExited;
        }
        finally
        {
            if (!keepShortcutForExplorer)
            {
                DeleteShortcutQuietly(shortcutPath);
            }
        }
    }

    private static void CreateServerShortcut(string shortcutPath, string serverExecutablePath)
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

            Type shortcutType = shortcut.GetType();
            string workingDirectory = Path.GetDirectoryName(serverExecutablePath) ?? Environment.CurrentDirectory;
            SetShortcutProperty(shortcutType, shortcut, "TargetPath", serverExecutablePath);
            SetShortcutProperty(shortcutType, shortcut, "Arguments", string.Empty);
            SetShortcutProperty(shortcutType, shortcut, "WorkingDirectory", workingDirectory);
            shortcutType.InvokeMember(
                "Save",
                BindingFlags.InvokeMethod,
                binder: null,
                target: shortcut,
                args: []);
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

    private static void SetShortcutProperty(Type shortcutType, object shortcut, string propertyName, object value)
    {
        shortcutType.InvokeMember(
            propertyName,
            BindingFlags.SetProperty,
            binder: null,
            target: shortcut,
            args: [value]);
    }

    private static string ResolveExplorerPath()
    {
        string windowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (string.IsNullOrWhiteSpace(windowsDirectory)
            && !string.IsNullOrWhiteSpace(Environment.SystemDirectory))
        {
            windowsDirectory = Directory.GetParent(Environment.SystemDirectory)?.FullName
                ?? Environment.SystemDirectory;
        }

        if (string.IsNullOrWhiteSpace(windowsDirectory))
        {
            windowsDirectory = @"C:\Windows";
        }

        return Path.Combine(windowsDirectory, "explorer.exe");
    }

    private static int DeleteExpiredShortcuts(
        string directory,
        DateTimeOffset now,
        TimeSpan maxAge)
    {
        if (!Directory.Exists(directory))
        {
            return 0;
        }

        int deleted = 0;
        string[] paths;
        try
        {
            paths = Directory.EnumerateFiles(
                directory,
                ShortcutPattern,
                SearchOption.TopDirectoryOnly).ToArray();
        }
        catch (IOException)
        {
            return 0;
        }
        catch (UnauthorizedAccessException)
        {
            return 0;
        }

        foreach (string path in paths)
        {
            DateTimeOffset lastWriteTime;
            try
            {
                lastWriteTime = File.GetLastWriteTimeUtc(path);
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            if (lastWriteTime > now || now - lastWriteTime < maxAge)
            {
                continue;
            }

            try
            {
                File.Delete(path);
                deleted++;
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return deleted;
    }

    private static void DeleteShortcutQuietly(string shortcutPath)
    {
        try
        {
            File.Delete(shortcutPath);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
