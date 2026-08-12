namespace TestGiveMeSpace.Core;

public interface IGuardPipeClient
{
    Task<GuardResponse> SendAsync(GuardCommand command, CancellationToken cancellationToken);

    Task<GuardResponse> SendAsync(
        GuardCommand command,
        GuardPurpose purpose,
        CancellationToken cancellationToken);

    Task<GuardResponse> SendAsync(
        GuardCommand command,
        GuardPurpose purpose,
        string? owner,
        CancellationToken cancellationToken);

    Task<GuardResponse> SendAsync(GuardRequest request, CancellationToken cancellationToken);
}
