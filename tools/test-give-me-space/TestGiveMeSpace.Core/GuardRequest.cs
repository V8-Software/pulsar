namespace TestGiveMeSpace.Core;

public sealed record GuardRequest(
    GuardCommand Command,
    GuardPurpose Purpose,
    string? Owner = null,
    int? X = null,
    int? Y = null)
{
    public static GuardRequest ForCommand(GuardCommand command)
        => new(command, GuardPurpose.Test);
}
