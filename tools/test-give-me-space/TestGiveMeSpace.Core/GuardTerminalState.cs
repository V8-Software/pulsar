namespace TestGiveMeSpace.Core;

public sealed record GuardTerminalState(
    GuardStatus Status,
    string? Owner = null,
    DateTimeOffset? StartedAtUtc = null);
