namespace TestGiveMeSpace.Core;

public interface IGuardCommandHandler
{
    Task<GuardResponse> HandleAsync(GuardRequest request, CancellationToken cancellationToken);
}
