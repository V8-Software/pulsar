namespace TestGiveMeSpace.Core;

public static class GuardPurposeExtensions
{
    public static string ToWireValue(this GuardPurpose purpose)
        => purpose switch
        {
            GuardPurpose.Test => "test",
            GuardPurpose.ObserveWindows => "observe-windows",
            _ => "unknown",
        };

    public static bool TryParseWireValue(string? value, out GuardPurpose purpose)
    {
        purpose = default;
        switch (value)
        {
            case "test":
                purpose = GuardPurpose.Test;
                return true;
            case "observe-windows":
                purpose = GuardPurpose.ObserveWindows;
                return true;
            default:
                return false;
        }
    }
}
