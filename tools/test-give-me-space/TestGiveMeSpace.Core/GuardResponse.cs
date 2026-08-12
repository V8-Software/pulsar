using System.Text.Json;
using System.Text.Json.Serialization;

namespace TestGiveMeSpace.Core;

public sealed record GuardResponse(
    GuardStatus Status,
    int ExitCode,
    string? Message = null,
    bool ShouldBeep = false,
    bool IsTerminalState = false,
    string? Owner = null,
    DateTimeOffset? StartedAtUtc = null)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static GuardResponse FromStatus(
        GuardStatus status,
        string? message = null,
        string? owner = null,
        DateTimeOffset? startedAtUtc = null)
    {
        GuardResponse response = status switch
        {
            GuardStatus.Idle => new(status, ExitCodes.Success, message),
            GuardStatus.Countdown => new(status, ExitCodes.Success, message),
            GuardStatus.Running => new(status, ExitCodes.Success, message),
            GuardStatus.ConfirmStop => new(status, ExitCodes.Success, message),
            GuardStatus.Granted => new(status, ExitCodes.Success, message),
            GuardStatus.Finished => new(status, ExitCodes.Success, message, ShouldBeep: true),
            GuardStatus.Cancelled => new(status, ExitCodes.Success, message),
            GuardStatus.CancelledByUser => new(status, ExitCodes.CancelledByUser, message ?? "Тестирование отменено пользователем"),
            GuardStatus.StoppedByUser => new(status, ExitCodes.StoppedByUser, message ?? "Тестирование остановлено пользователем", IsTerminalState: true),
            GuardStatus.ClosedByUser => new(status, ExitCodes.ClosedByUser, message ?? "Тестирование закрыто пользователем", IsTerminalState: true),
            GuardStatus.ClosedByTimeout => new(status, ExitCodes.ClosedByTimeout, message ?? "Плашка закрыта по таймауту", IsTerminalState: true),
            GuardStatus.BusyCountdown => new(status, ExitCodes.Busy, message ?? "Уже идёт подготовка к тестированию"),
            GuardStatus.BusyRunning => new(status, ExitCodes.Busy, message ?? "Уже идёт тестирование"),
            GuardStatus.BusyConfirmStop => new(status, ExitCodes.Busy, message ?? "Идёт подтверждение остановки тестирования"),
            GuardStatus.OwnerMismatch => new(status, ExitCodes.Busy, message ?? "Охраняемый участок принадлежит другому владельцу"),
            GuardStatus.IpcError => new(status, ExitCodes.IpcError, message ?? "Сервер test-give-me-space не отвечает"),
            GuardStatus.ServerNotReady => new(status, ExitCodes.IpcError, message ?? "Сервер test-give-me-space не готов"),
            GuardStatus.ProtocolError => new(status, ExitCodes.ProtocolError, message ?? "Некорректный ответ сервера test-give-me-space"),
            GuardStatus.IoError => new(status, ExitCodes.IoError, message ?? "Ошибка чтения или записи состояния test-give-me-space"),
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
        };

        return response with
        {
            Owner = NormalizeOwner(owner),
            StartedAtUtc = startedAtUtc?.ToUniversalTime(),
        };
    }

    public string ToJson()
        => JsonSerializer.Serialize(
            new JsonShape(Status.ToWireValue(), Message, Owner, StartedAtUtc),
            JsonOptions);

    private static string? NormalizeOwner(string? owner)
        => string.IsNullOrWhiteSpace(owner) ? null : owner.Trim();

    private sealed record JsonShape(
        [property: JsonPropertyName("status")] string Status,
        [property: JsonPropertyName("message")] string? Message,
        [property: JsonPropertyName("owner")] string? Owner,
        [property: JsonPropertyName("startedAtUtc")] DateTimeOffset? StartedAtUtc);
}
