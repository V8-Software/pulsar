using System.Text.Json;
using System.Text.Json.Serialization;

namespace TestGiveMeSpace.Core;

public sealed class StateStore
{
    private const int CurrentVersion = 2;
    private readonly string _path;

    public StateStore(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _path = Path.Combine(directory, "state.json");
    }

    public GuardStatus ReadTerminalState()
        => ReadTerminalStateInfo().Status;

    public GuardTerminalState ReadTerminalStateInfo()
    {
        if (!File.Exists(_path))
        {
            return new GuardTerminalState(GuardStatus.Idle);
        }

        try
        {
            var json = File.ReadAllText(_path);
            var state = JsonSerializer.Deserialize<StateShape>(json);
            GuardStatus status = state?.Status switch
            {
                "stopped_by_user" => GuardStatus.StoppedByUser,
                "closed_by_user" => GuardStatus.ClosedByUser,
                "closed_by_timeout" => GuardStatus.ClosedByTimeout,
                _ => GuardStatus.Idle,
            };

            return status == GuardStatus.Idle
                ? new GuardTerminalState(status)
                : new GuardTerminalState(
                    status,
                    NormalizeOwner(state?.Owner),
                    state?.StartedAtUtc?.ToUniversalTime());
        }
        catch (IOException)
        {
            return new GuardTerminalState(GuardStatus.Idle);
        }
        catch (UnauthorizedAccessException)
        {
            return new GuardTerminalState(GuardStatus.Idle);
        }
        catch (JsonException)
        {
            return new GuardTerminalState(GuardStatus.Idle);
        }
    }

    public void WriteTerminalState(GuardStatus status)
        => WriteTerminalState(status, owner: null, startedAtUtc: null);

    public void WriteTerminalState(
        GuardStatus status,
        string? owner,
        DateTimeOffset? startedAtUtc)
    {
        if (status is not (
            GuardStatus.StoppedByUser
            or GuardStatus.ClosedByUser
            or GuardStatus.ClosedByTimeout))
        {
            Clear();
            return;
        }

        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string json = JsonSerializer.Serialize(new StateShape(
            CurrentVersion,
            status.ToWireValue(),
            NormalizeOwner(owner),
            startedAtUtc?.ToUniversalTime()));
        string temporaryPath = Path.Combine(
            directory ?? string.Empty,
            $"state.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporaryPath, json);
            File.Move(temporaryPath, _path, overwrite: true);
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    public void Clear()
    {
        try
        {
            if (File.Exists(_path))
            {
                File.Delete(_path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static string? NormalizeOwner(string? owner)
        => string.IsNullOrWhiteSpace(owner) ? null : owner.Trim();

    private sealed record StateShape(
        [property: JsonPropertyName("version")] int Version,
        [property: JsonPropertyName("status")] string Status,
        [property: JsonPropertyName("owner")] string? Owner = null,
        [property: JsonPropertyName("startedAtUtc")] DateTimeOffset? StartedAtUtc = null);
}
