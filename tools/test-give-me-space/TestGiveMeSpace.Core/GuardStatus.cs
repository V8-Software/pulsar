namespace TestGiveMeSpace.Core;

public enum GuardStatus
{
    Idle,
    Countdown,
    Running,
    ConfirmStop,
    Granted,
    Finished,
    Cancelled,
    CancelledByUser,
    StoppedByUser,
    ClosedByUser,
    ClosedByTimeout,
    BusyCountdown,
    BusyRunning,
    BusyConfirmStop,
    OwnerMismatch,
    IpcError,
    ServerNotReady,
    ProtocolError,
    IoError,
}
