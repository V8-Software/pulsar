namespace TestGiveMeSpace.Core;

public static class GuardCommandExtensions
{
    public static string ToWireValue(this GuardCommand command)
        => command switch
        {
            GuardCommand.Request => "request",
            GuardCommand.Status => "status",
            GuardCommand.Finish => "finish",
            GuardCommand.Cancel => "cancel",
            GuardCommand.Hide => "hide",
            GuardCommand.Show => "show",
            GuardCommand.AvoidPoint => "avoid-point",
            GuardCommand.RestorePosition => "restore-position",
            _ => throw new ArgumentOutOfRangeException(nameof(command), command, null),
        };

    public static bool TryParseWireValue(string? value, out GuardCommand command)
    {
        command = value switch
        {
            "request" => GuardCommand.Request,
            "status" => GuardCommand.Status,
            "finish" => GuardCommand.Finish,
            "cancel" => GuardCommand.Cancel,
            "hide" => GuardCommand.Hide,
            "show" => GuardCommand.Show,
            "avoid-point" => GuardCommand.AvoidPoint,
            "restore-position" => GuardCommand.RestorePosition,
            _ => default,
        };

        return value is
            "request" or
            "status" or
            "finish" or
            "cancel" or
            "hide" or
            "show" or
            "avoid-point" or
            "restore-position";
    }
}
