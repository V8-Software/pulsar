using System.Text.Json;
using System.Text.Json.Serialization;

namespace TestGiveMeSpace.Core;

public sealed record GuardPosition(double Left, double Top);

public sealed record GuardSettings(
    double? Left,
    double? Top,
    IReadOnlyDictionary<string, GuardPosition>? MonitorPositions = null);

public sealed class SettingsStore
{
    private const int CurrentVersion = 2;
    private readonly string _path;

    public SettingsStore(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _path = Path.Combine(directory, "settings.json");
    }

    public GuardSettings Load()
    {
        if (!File.Exists(_path))
        {
            return new GuardSettings(null, null);
        }

        try
        {
            var json = File.ReadAllText(_path);
            var settings = JsonSerializer.Deserialize<SettingsShape>(json);
            if (settings?.Version == 1)
            {
                return new GuardSettings(settings.Left, settings.Top);
            }

            if (settings?.Version != CurrentVersion)
            {
                return new GuardSettings(null, null);
            }

            Dictionary<string, GuardPosition> monitorPositions = new(StringComparer.OrdinalIgnoreCase);
            if (settings.MonitorPositions is not null)
            {
                foreach ((string monitor, PositionShape position) in settings.MonitorPositions)
                {
                    monitorPositions[monitor] = new GuardPosition(position.Left, position.Top);
                }
            }

            return new GuardSettings(settings.Left, settings.Top, monitorPositions);
        }
        catch (IOException)
        {
            return new GuardSettings(null, null);
        }
        catch (UnauthorizedAccessException)
        {
            return new GuardSettings(null, null);
        }
        catch (JsonException)
        {
            return new GuardSettings(null, null);
        }
    }

    public void Save(GuardSettings settings)
    {
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        Dictionary<string, PositionShape>? monitorPositions = settings.MonitorPositions?.ToDictionary(
            pair => pair.Key,
            pair => new PositionShape(pair.Value.Left, pair.Value.Top),
            StringComparer.OrdinalIgnoreCase);
        var json = JsonSerializer.Serialize(new SettingsShape(
            CurrentVersion,
            settings.Left,
            settings.Top,
            monitorPositions));
        File.WriteAllText(_path, json);
    }

    public void SavePrimaryPosition(double left, double top)
    {
        GuardSettings settings = Load();
        Save(settings with { Left = left, Top = top });
    }

    public void SaveMonitorPosition(string monitor, double left, double top)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(monitor);
        GuardSettings settings = Load();
        Dictionary<string, GuardPosition> monitorPositions = new(
            settings.MonitorPositions ?? new Dictionary<string, GuardPosition>(),
            StringComparer.OrdinalIgnoreCase)
        {
            [monitor] = new GuardPosition(left, top),
        };
        Save(settings with { MonitorPositions = monitorPositions });
    }

    private sealed record SettingsShape(
        [property: JsonPropertyName("version")] int Version,
        [property: JsonPropertyName("left")] double? Left,
        [property: JsonPropertyName("top")] double? Top,
        [property: JsonPropertyName("monitorPositions")] Dictionary<string, PositionShape>? MonitorPositions = null);

    private sealed record PositionShape(
        [property: JsonPropertyName("left")] double Left,
        [property: JsonPropertyName("top")] double Top);
}
