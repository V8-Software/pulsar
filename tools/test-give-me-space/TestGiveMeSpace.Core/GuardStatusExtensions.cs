namespace TestGiveMeSpace.Core;

public static class GuardStatusExtensions
{
    public static string ToWireValue(this GuardStatus status)
        => status switch
        {
            GuardStatus.Idle => "idle",
            GuardStatus.Countdown => "countdown",
            GuardStatus.Running => "running",
            GuardStatus.ConfirmStop => "confirm_stop",
            GuardStatus.Granted => "granted",
            GuardStatus.Finished => "finished",
            GuardStatus.Cancelled => "cancelled",
            GuardStatus.CancelledByUser => "cancelled_by_user",
            GuardStatus.StoppedByUser => "stopped_by_user",
            GuardStatus.ClosedByUser => "closed_by_user",
            GuardStatus.ClosedByTimeout => "closed_by_timeout",
            GuardStatus.BusyCountdown => "busy_countdown",
            GuardStatus.BusyRunning => "busy_running",
            GuardStatus.BusyConfirmStop => "busy_confirm_stop",
            GuardStatus.OwnerMismatch => "owner_mismatch",
            GuardStatus.IpcError => "ipc_error",
            GuardStatus.ServerNotReady => "server_not_ready",
            GuardStatus.ProtocolError => "protocol_error",
            GuardStatus.IoError => "io_error",
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
        };

    public static bool TryParseWireValue(string? value, out GuardStatus status)
    {
        status = value switch
        {
            "idle" => GuardStatus.Idle,
            "countdown" => GuardStatus.Countdown,
            "running" => GuardStatus.Running,
            "confirm_stop" => GuardStatus.ConfirmStop,
            "granted" => GuardStatus.Granted,
            "finished" => GuardStatus.Finished,
            "cancelled" => GuardStatus.Cancelled,
            "cancelled_by_user" => GuardStatus.CancelledByUser,
            "stopped_by_user" => GuardStatus.StoppedByUser,
            "closed_by_user" => GuardStatus.ClosedByUser,
            "closed_by_timeout" => GuardStatus.ClosedByTimeout,
            "busy_countdown" => GuardStatus.BusyCountdown,
            "busy_running" => GuardStatus.BusyRunning,
            "busy_confirm_stop" => GuardStatus.BusyConfirmStop,
            "owner_mismatch" => GuardStatus.OwnerMismatch,
            "ipc_error" => GuardStatus.IpcError,
            "server_not_ready" => GuardStatus.ServerNotReady,
            "protocol_error" => GuardStatus.ProtocolError,
            "io_error" => GuardStatus.IoError,
            _ => default,
        };

        return value is
            "idle" or
            "countdown" or
            "running" or
            "confirm_stop" or
            "granted" or
            "finished" or
            "cancelled" or
            "cancelled_by_user" or
            "stopped_by_user" or
            "closed_by_user" or
            "closed_by_timeout" or
            "busy_countdown" or
            "busy_running" or
            "busy_confirm_stop" or
            "owner_mismatch" or
            "ipc_error" or
            "server_not_ready" or
            "protocol_error" or
            "io_error";
    }
}
