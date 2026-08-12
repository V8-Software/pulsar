namespace TestGiveMeSpace.Core;

public static class ExitCodes
{
    public const int Success = 0;
    public const int CancelledByUser = 20;
    public const int StoppedByUser = 21;
    public const int Busy = 22;
    public const int ClosedByUser = 23;
    public const int ClosedByTimeout = 24;

    public const int IpcError = 50;
    public const int ProtocolError = 51;
    public const int IoError = 52;
}
