using TestGiveMeSpace.Core;

namespace TestGiveMeSpace.Tests;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "tgms-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Missing_settings_return_default_position()
    {
        var store = new SettingsStore(_dir);

        var settings = store.Load();

        Assert.Null(settings.Left);
        Assert.Null(settings.Top);
    }

    [Fact]
    public void Corrupted_settings_return_default_position()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "settings.json"), "{broken");
        var store = new SettingsStore(_dir);

        var settings = store.Load();

        Assert.Null(settings.Left);
        Assert.Null(settings.Top);
    }

    [Fact]
    public void Saves_and_loads_position()
    {
        var store = new SettingsStore(_dir);

        store.Save(new GuardSettings(42, 24));

        var settings = store.Load();
        Assert.Equal(42, settings.Left);
        Assert.Equal(24, settings.Top);
    }

    [Fact]
    public void Saves_independent_positions_for_each_monitor()
    {
        var store = new SettingsStore(_dir);

        store.SaveMonitorPosition(@"\\.\DISPLAY1", 100, 200);
        store.SaveMonitorPosition(@"\\.\DISPLAY2", 2100, 300);

        var settings = store.Load();
        Assert.Equal(new GuardPosition(100, 200), settings.MonitorPositions![@"\\.\DISPLAY1"]);
        Assert.Equal(new GuardPosition(2100, 300), settings.MonitorPositions![@"\\.\DISPLAY2"]);
    }

    [Fact]
    public void Saving_primary_position_preserves_monitor_positions()
    {
        var store = new SettingsStore(_dir);
        store.SaveMonitorPosition(@"\\.\DISPLAY2", 2100, 300);

        store.SavePrimaryPosition(42, 24);

        var settings = store.Load();
        Assert.Equal(42, settings.Left);
        Assert.Equal(24, settings.Top);
        Assert.Equal(new GuardPosition(2100, 300), settings.MonitorPositions![@"\\.\DISPLAY2"]);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }
}
