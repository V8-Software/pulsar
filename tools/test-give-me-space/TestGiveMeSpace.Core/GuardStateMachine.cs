namespace TestGiveMeSpace.Core;

public sealed class GuardStateMachine
{
    public GuardStatus State { get; private set; } = GuardStatus.Idle;

    private string? owner;
    private DateTimeOffset? startedAtUtc;

    public GuardResponse Request(string? requestOwner = null, DateTimeOffset? requestStartedAtUtc = null)
    {
        return State switch
        {
            GuardStatus.Idle => StartRequest(requestOwner, requestStartedAtUtc),
            GuardStatus.StoppedByUser
                or GuardStatus.ClosedByUser
                or GuardStatus.ClosedByTimeout => StartRequest(requestOwner, requestStartedAtUtc),
            GuardStatus.Countdown => AttachRunMetadata(GuardResponse.FromStatus(GuardStatus.BusyCountdown)),
            GuardStatus.Running => AttachRunMetadata(GuardResponse.FromStatus(GuardStatus.BusyRunning)),
            GuardStatus.ConfirmStop => AttachRunMetadata(GuardResponse.FromStatus(GuardStatus.BusyConfirmStop)),
            _ => AttachRunMetadata(GuardResponse.FromStatus(GuardStatus.BusyRunning)),
        };
    }

    public GuardResponse Status()
        => AttachRunMetadata(GuardResponse.FromStatus(State));

    public GuardResponse CompleteCountdown()
    {
        if (State != GuardStatus.Countdown)
        {
            return AttachRunMetadata(GuardResponse.FromStatus(State));
        }

        State = GuardStatus.Running;
        return AttachRunMetadata(GuardResponse.FromStatus(GuardStatus.Granted));
    }

    public GuardResponse LeftClick()
    {
        return State switch
        {
            GuardStatus.Countdown => CancelCountdownByUser(),
            GuardStatus.Running => SetState(GuardStatus.ConfirmStop),
            GuardStatus.ConfirmStop => SetState(GuardStatus.StoppedByUser),
            _ => AttachRunMetadata(GuardResponse.FromStatus(State)),
        };
    }

    public GuardResponse ConfirmStopTimedOut()
    {
        if (State != GuardStatus.ConfirmStop)
        {
            return AttachRunMetadata(GuardResponse.FromStatus(State));
        }

        return SetState(GuardStatus.Running);
    }

    public GuardResponse Finish(string? requestOwner = null, DateTimeOffset? finishedAtUtc = null)
    {
        GuardResponse? ownerMismatch = RejectOwnerMismatch(requestOwner);
        if (ownerMismatch is not null)
        {
            return ownerMismatch;
        }

        return State switch
        {
            GuardStatus.Running or GuardStatus.ConfirmStop => FinishRunning(finishedAtUtc),
            GuardStatus.Countdown => SetState(GuardStatus.Idle, GuardStatus.Cancelled),
            GuardStatus.StoppedByUser => AttachRunMetadata(GuardResponse.FromStatus(GuardStatus.StoppedByUser)),
            GuardStatus.ClosedByUser => AttachRunMetadata(GuardResponse.FromStatus(GuardStatus.ClosedByUser)),
            GuardStatus.ClosedByTimeout => AttachRunMetadata(GuardResponse.FromStatus(GuardStatus.ClosedByTimeout)),
            _ => GuardResponse.FromStatus(GuardStatus.Idle),
        };
    }

    public GuardResponse CloseByUser()
        => SetState(GuardStatus.ClosedByUser);

    public GuardResponse CloseByTimeout()
        => SetState(GuardStatus.ClosedByTimeout);

    public GuardResponse Cancel(string? requestOwner = null)
    {
        GuardResponse? ownerMismatch = RejectOwnerMismatch(requestOwner);
        if (ownerMismatch is not null)
        {
            return ownerMismatch;
        }

        return SetState(GuardStatus.Idle, GuardStatus.Cancelled);
    }

    public GuardResponse? ValidateRunningOwner(string? requestOwner)
    {
        string? normalizedRequestOwner = NormalizeOwner(requestOwner);
        if (normalizedRequestOwner is null
            || !string.Equals(owner, normalizedRequestOwner, StringComparison.Ordinal))
        {
            return AttachRunMetadata(GuardResponse.FromStatus(GuardStatus.OwnerMismatch));
        }

        if (State != GuardStatus.Running)
        {
            return AttachRunMetadata(GuardResponse.FromStatus(State));
        }

        return null;
    }

    private GuardResponse CancelCountdownByUser()
    {
        GuardResponse response = AttachRunMetadata(GuardResponse.FromStatus(GuardStatus.CancelledByUser));
        State = GuardStatus.Idle;
        ClearRunMetadata();
        return response;
    }

    private GuardResponse FinishRunning(DateTimeOffset? finishedAtUtc)
    {
        GuardResponse response = AttachRunMetadata(GuardResponse.FromStatus(GuardStatus.Finished));
        if (!ShouldBeepOnFinish(finishedAtUtc))
        {
            response = response with { ShouldBeep = false };
        }

        State = GuardStatus.Idle;
        ClearRunMetadata();
        return response;
    }

    private GuardResponse SetState(GuardStatus state)
        => SetState(state, state);

    private GuardResponse SetState(GuardStatus state, GuardStatus responseStatus)
    {
        GuardResponse response = AttachRunMetadata(GuardResponse.FromStatus(responseStatus));
        State = state;
        if (state == GuardStatus.Idle)
        {
            ClearRunMetadata();
        }

        return response;
    }

    private GuardResponse StartRequest(
        string? requestOwner,
        DateTimeOffset? requestStartedAtUtc)
    {
        State = GuardStatus.Countdown;
        owner = NormalizeOwner(requestOwner);
        startedAtUtc = (requestStartedAtUtc ?? DateTimeOffset.UtcNow).ToUniversalTime();
        return AttachRunMetadata(GuardResponse.FromStatus(GuardStatus.Countdown));
    }

    private GuardResponse? RejectOwnerMismatch(string? requestOwner)
    {
        if (owner is null)
        {
            return null;
        }

        string? normalizedOwner = NormalizeOwner(requestOwner);
        return string.Equals(owner, normalizedOwner, StringComparison.Ordinal)
            ? null
            : AttachRunMetadata(GuardResponse.FromStatus(GuardStatus.OwnerMismatch));
    }

    private GuardResponse AttachRunMetadata(GuardResponse response)
        => response with
        {
            Owner = owner,
            StartedAtUtc = startedAtUtc,
        };

    private void ClearRunMetadata()
    {
        owner = null;
        startedAtUtc = null;
    }

    private static string? NormalizeOwner(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private bool ShouldBeepOnFinish(DateTimeOffset? finishedAtUtc)
    {
        if (startedAtUtc is null)
        {
            return true;
        }

        DateTimeOffset finished = (finishedAtUtc ?? DateTimeOffset.UtcNow).ToUniversalTime();
        return finished - startedAtUtc.Value >= TimeSpan.FromMinutes(1);
    }
}
