namespace TestGiveMeSpace.Core;

public sealed class GuardCommandRunner(
    IGuardPipeClient pipeClient,
    IGuardServerProcess serverProcess,
    StateStore stateStore)
{
    private static readonly TimeSpan FinishShutdownWait = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan StatusShutdownRetry = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan StatusRetryDelay = TimeSpan.FromMilliseconds(100);

    public async Task<GuardResponse> ExecuteAsync(
        GuardCommand command,
        CancellationToken cancellationToken,
        GuardPurpose purpose = GuardPurpose.Test,
        string? owner = null)
    {
        return command switch
        {
            GuardCommand.Request => await RequestAsync(cancellationToken, purpose, owner),
            GuardCommand.Status => await StatusAsync(cancellationToken),
            GuardCommand.Finish => await FinishAsync(cancellationToken, owner),
            GuardCommand.Cancel => await CancelAsync(cancellationToken, owner),
            GuardCommand.Hide => await SendRunningOwnerCommandAsync(cancellationToken, GuardCommand.Hide, owner),
            GuardCommand.Show => await SendRunningOwnerCommandAsync(cancellationToken, GuardCommand.Show, owner),
            GuardCommand.AvoidPoint => GuardResponse.FromStatus(GuardStatus.ProtocolError),
            GuardCommand.RestorePosition => await RelocateAsync(
                cancellationToken,
                GuardCommand.RestorePosition,
                owner,
                x: null,
                y: null),
            _ => GuardResponse.FromStatus(GuardStatus.ProtocolError),
        };
    }

    public async Task<GuardResponse> ExecuteAsync(
        GuardRequest request,
        CancellationToken cancellationToken)
    {
        return request.Command switch
        {
            GuardCommand.Hide or GuardCommand.Show => await SendRunningOwnerCommandAsync(
                cancellationToken,
                request.Command,
                request.Owner),
            GuardCommand.AvoidPoint => await RelocateAsync(
                cancellationToken,
                GuardCommand.AvoidPoint,
                request.Owner,
                request.X,
                request.Y),
            GuardCommand.RestorePosition => await RelocateAsync(
                cancellationToken,
                GuardCommand.RestorePosition,
                request.Owner,
                request.X,
                request.Y),
            _ => await ExecuteAsync(
                request.Command,
                cancellationToken,
                request.Purpose,
                request.Owner),
        };
    }

    private async Task<GuardResponse> RelocateAsync(
        CancellationToken cancellationToken,
        GuardCommand command,
        string? owner,
        int? x,
        int? y)
    {
        if ((command == GuardCommand.AvoidPoint && (!x.HasValue || !y.HasValue))
            || (command == GuardCommand.RestorePosition && (x.HasValue || y.HasValue)))
        {
            return GuardResponse.FromStatus(GuardStatus.ProtocolError);
        }

        if (!serverProcess.IsRunning())
        {
            return GuardResponse.FromStatus(GuardStatus.Idle);
        }

        GuardRequest request = new(command, GuardPurpose.Test, owner, x, y);
        return await pipeClient.SendAsync(request, cancellationToken);
    }

    private async Task<GuardResponse> SendRunningOwnerCommandAsync(
        CancellationToken cancellationToken,
        GuardCommand command,
        string? owner)
    {
        if (!serverProcess.IsRunning())
        {
            return GuardResponse.FromStatus(GuardStatus.Idle);
        }

        return await pipeClient.SendAsync(
            new GuardRequest(command, GuardPurpose.Test, owner),
            cancellationToken);
    }

    private async Task<GuardResponse> RequestAsync(
        CancellationToken cancellationToken,
        GuardPurpose purpose,
        string? owner)
    {
        if (!serverProcess.IsRunning())
        {
            try
            {
                serverProcess.EnsureStarted();
            }
            catch (Exception ex)
            {
                return GuardResponse.FromStatus(
                    GuardStatus.IpcError,
                    $"Сервер test-give-me-space не запустился: {ex.Message}");
            }
        }

        GuardResponse response = await pipeClient.SendAsync(
            GuardCommand.Request,
            purpose,
            owner,
            cancellationToken);
        if (response.Status is not (GuardStatus.ServerNotReady
            or GuardStatus.IpcError
            or GuardStatus.ProtocolError
            or GuardStatus.IoError))
        {
            stateStore.Clear();
        }

        return response;
    }

    private async Task<GuardResponse> StatusAsync(CancellationToken cancellationToken)
    {
        if (!serverProcess.IsRunning())
        {
            return ReadTerminalStateResponse();
        }

        GuardResponse response = await pipeClient.SendAsync(GuardCommand.Status, cancellationToken);
        return IsPipeUnavailable(response)
            ? await ResolveStatusAfterPipeFailureAsync(cancellationToken)
            : response;
    }

    private async Task<GuardResponse> FinishAsync(
        CancellationToken cancellationToken,
        string? owner)
    {
        if (!serverProcess.IsRunning())
        {
            GuardTerminalState terminalState = stateStore.ReadTerminalStateInfo();
            return GuardResponse.FromStatus(
                terminalState.Status,
                owner: terminalState.Owner,
                startedAtUtc: terminalState.StartedAtUtc);
        }

        GuardResponse response = await pipeClient.SendAsync(
            GuardCommand.Finish,
            GuardPurpose.Test,
            owner,
            cancellationToken);
        if (response.Status == GuardStatus.Finished)
        {
            try
            {
                await serverProcess.WaitUntilStoppedAsync(FinishShutdownWait, cancellationToken);
            }
            catch (OperationCanceledException)
            {
            }

            return response;
        }

        return IsPipeUnavailable(response) && !serverProcess.IsRunning()
            ? ReadTerminalStateResponse()
            : NormalizePipeUnavailable(response);
    }

    private async Task<GuardResponse> CancelAsync(
        CancellationToken cancellationToken,
        string? owner)
    {
        if (!serverProcess.IsRunning())
        {
            stateStore.Clear();
            return GuardResponse.FromStatus(GuardStatus.Cancelled);
        }

        GuardResponse response = await pipeClient.SendAsync(
            GuardCommand.Cancel,
            GuardPurpose.Test,
            owner,
            cancellationToken);
        stateStore.Clear();
        return response.Status == GuardStatus.ServerNotReady
            ? GuardResponse.FromStatus(GuardStatus.Cancelled)
            : response;
    }

    private async Task<GuardResponse> ResolveStatusAfterPipeFailureAsync(
        CancellationToken cancellationToken)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow.Add(StatusShutdownRetry);
        while (true)
        {
            if (!serverProcess.IsRunning())
            {
                return ReadTerminalStateResponse();
            }

            TimeSpan remaining = deadline - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                return GuardResponse.FromStatus(GuardStatus.IpcError);
            }

            await Task.Delay(Min(StatusRetryDelay, remaining), cancellationToken);
            if (!serverProcess.IsRunning())
            {
                return ReadTerminalStateResponse();
            }

            GuardResponse retry = await SendStatusRetryAsync(deadline, cancellationToken);
            if (!IsPipeUnavailable(retry))
            {
                return retry;
            }
        }
    }

    private async Task<GuardResponse> SendStatusRetryAsync(
        DateTimeOffset deadline,
        CancellationToken cancellationToken)
    {
        TimeSpan remaining = deadline - DateTimeOffset.UtcNow;
        if (remaining <= TimeSpan.Zero)
        {
            return GuardResponse.FromStatus(GuardStatus.IpcError);
        }

        using CancellationTokenSource retryCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        retryCancellation.CancelAfter(remaining);
        try
        {
            return await pipeClient
                .SendAsync(GuardCommand.Status, retryCancellation.Token)
                .WaitAsync(remaining, cancellationToken);
        }
        catch (TimeoutException)
        {
            return GuardResponse.FromStatus(GuardStatus.IpcError);
        }
        catch (OperationCanceledException) when (
            retryCancellation.IsCancellationRequested
            && !cancellationToken.IsCancellationRequested)
        {
            return GuardResponse.FromStatus(GuardStatus.IpcError);
        }
    }

    private GuardResponse ReadTerminalStateResponse()
    {
        GuardTerminalState terminalState = stateStore.ReadTerminalStateInfo();
        return GuardResponse.FromStatus(
            terminalState.Status,
            owner: terminalState.Owner,
            startedAtUtc: terminalState.StartedAtUtc);
    }

    private static GuardResponse NormalizePipeUnavailable(GuardResponse response)
        => response.Status == GuardStatus.ServerNotReady
            ? GuardResponse.FromStatus(GuardStatus.IpcError)
            : response;

    private static bool IsPipeUnavailable(GuardResponse response)
        => response.Status is GuardStatus.ServerNotReady or GuardStatus.IpcError;

    private static TimeSpan Min(TimeSpan first, TimeSpan second)
        => first <= second ? first : second;
}
