using System.Windows;
using System.Windows.Threading;
using TestGiveMeSpace.Core;

namespace TestGiveMeSpace.Server;

internal sealed class GuardServerSession : IGuardCommandHandler
{
    private const int CountdownSeconds = 10;
    private static readonly TimeSpan CountdownTick = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan ConfirmStopTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan GuardLifetime = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan ShutdownDelay = TimeSpan.FromMilliseconds(700);

    private readonly Dispatcher dispatcher;
    private readonly IPlaqueWindowGroup window;
    private readonly StateStore stateStore;
    private readonly GuardStateMachine stateMachine;
    private DispatcherTimer? countdownTimer;
    private DispatcherTimer? confirmStopTimer;
    private DispatcherTimer? guardLifetimeTimer;
    private TaskCompletionSource<GuardResponse>? pendingRequest;
    private int countdownRemaining;
    private GuardDisplayTexts displayTexts = GuardDisplayTexts.For(GuardPurpose.Test);
    private bool shutdownScheduled;

    public GuardServerSession(
        Dispatcher dispatcher,
        IPlaqueWindowGroup window,
        StateStore stateStore)
        : this(dispatcher, window, stateStore, new GuardStateMachine())
    {
    }

    internal GuardServerSession(
        Dispatcher dispatcher,
        IPlaqueWindowGroup window,
        StateStore stateStore,
        GuardStateMachine stateMachine)
    {
        this.dispatcher = dispatcher;
        this.window = window;
        this.stateStore = stateStore;
        this.stateMachine = stateMachine;
        window.LeftClickRequested += (_, _) => HandleLeftClick();
        window.CloseRequested += (_, _) => HandleCloseByUser();
    }

    public async Task<GuardResponse> HandleAsync(
        GuardRequest request,
        CancellationToken cancellationToken)
    {
        if (dispatcher.CheckAccess())
        {
            return await HandleOnDispatcherAsync(request);
        }

        Task<GuardResponse> operation = await dispatcher.InvokeAsync(
            () => HandleOnDispatcherAsync(request),
            DispatcherPriority.Send,
            cancellationToken);
        return await operation;
    }

    private Task<GuardResponse> HandleOnDispatcherAsync(GuardRequest request)
    {
        return request.Command switch
        {
            GuardCommand.Request => RequestAsync(request.Purpose, request.Owner),
            GuardCommand.Status => Task.FromResult(stateMachine.Status()),
            GuardCommand.Finish => Task.FromResult(Finish(request.Owner)),
            GuardCommand.Cancel => Task.FromResult(Cancel(request.Owner)),
            GuardCommand.Hide => Task.FromResult(Hide(request.Owner)),
            GuardCommand.Show => Task.FromResult(Show(request.Owner)),
            GuardCommand.AvoidPoint => Task.FromResult(AvoidPoint(request.Owner, request.X, request.Y)),
            GuardCommand.RestorePosition => Task.FromResult(RestorePosition(request.Owner)),
            _ => Task.FromResult(GuardResponse.FromStatus(GuardStatus.ProtocolError)),
        };
    }

    private GuardResponse AvoidPoint(string? owner, int? x, int? y)
    {
        if (!x.HasValue || !y.HasValue)
        {
            return GuardResponse.FromStatus(GuardStatus.ProtocolError);
        }

        GuardResponse? ownerMismatch = RejectOwnerMismatch(owner);
        if (ownerMismatch is not null)
        {
            return ownerMismatch;
        }

        return window.AvoidPoint(x.Value, y.Value)
            ? GuardResponse.FromStatus(GuardStatus.Granted)
            : GuardResponse.FromStatus(GuardStatus.IpcError);
    }

    private GuardResponse RestorePosition(string? owner)
    {
        GuardResponse? ownerMismatch = RejectOwnerMismatch(owner);
        if (ownerMismatch is not null)
        {
            return ownerMismatch;
        }

        return window.RestorePositions()
            ? GuardResponse.FromStatus(GuardStatus.Granted)
            : GuardResponse.FromStatus(GuardStatus.IpcError);
    }

    private GuardResponse Hide(string? owner)
    {
        GuardResponse? ownerMismatch = RejectOwnerMismatch(owner);
        if (ownerMismatch is not null)
            return ownerMismatch;

        window.HidePlaque();
        return GuardResponse.FromStatus(GuardStatus.Granted);
    }

    private GuardResponse Show(string? owner)
    {
        GuardResponse? ownerMismatch = RejectOwnerMismatch(owner);
        if (ownerMismatch is not null)
            return ownerMismatch;

        window.ShowPlaque();
        return GuardResponse.FromStatus(GuardStatus.Granted);
    }

    private GuardResponse? RejectOwnerMismatch(string? requestOwner)
        => stateMachine.ValidateRunningOwner(requestOwner);

    private Task<GuardResponse> RequestAsync(GuardPurpose purpose, string? owner)
    {
        GuardResponse response = stateMachine.Request(owner);
        if (response.Status != GuardStatus.Countdown)
        {
            return Task.FromResult(response);
        }

        stateStore.Clear();
        displayTexts = GuardDisplayTexts.For(purpose);
        pendingRequest = new TaskCompletionSource<GuardResponse>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        StartCountdown();
        return pendingRequest.Task;
    }

    private void StartCountdown()
    {
        StopTimers();
        countdownRemaining = CountdownSeconds;
        UpdateCountdownText();
        window.ShowPlaque();
        NativeMethods.PlayGuardSound();
        StartGuardLifetimeTimer();

        countdownTimer = new DispatcherTimer(CountdownTick, DispatcherPriority.Normal, (_, _) =>
        {
            countdownRemaining--;
            if (countdownRemaining <= 0)
            {
                CompleteCountdown();
            }
            else
            {
                UpdateCountdownText();
            }
        }, dispatcher);
        countdownTimer.Start();
    }

    private void CompleteCountdown()
    {
        countdownTimer?.Stop();
        countdownTimer = null;
        GuardResponse response = stateMachine.CompleteCountdown();
        window.SetDisplayText(displayTexts.Running);
        pendingRequest?.TrySetResult(response);
        pendingRequest = null;
    }

    private void UpdateCountdownText()
        => window.SetDisplayText(displayTexts.CountdownText(countdownRemaining));

    private GuardResponse Finish(string? owner)
    {
        GuardResponse response = stateMachine.Finish(owner);
        if (response.Status == GuardStatus.OwnerMismatch)
        {
            return response;
        }

        StopTimers();
        if (response.Status == GuardStatus.Cancelled)
        {
            stateStore.Clear();
            pendingRequest?.TrySetResult(response);
            pendingRequest = null;
            window.HidePlaque();
            ScheduleShutdown();
        }

        if (response.Status == GuardStatus.Finished)
        {
            stateStore.Clear();
            window.HidePlaque();
            if (response.ShouldBeep)
            {
                NativeMethods.PlayGuardSound();
            }

            ScheduleShutdown();
        }

        return response;
    }

    private GuardResponse Cancel(string? owner)
    {
        GuardResponse response = stateMachine.Cancel(owner);
        if (response.Status == GuardStatus.OwnerMismatch)
        {
            return response;
        }

        StopTimers();
        stateStore.Clear();
        pendingRequest?.TrySetResult(response);
        pendingRequest = null;
        window.HidePlaque();
        ScheduleShutdown();
        return response;
    }

    private void HandleLeftClick()
    {
        GuardResponse response = stateMachine.LeftClick();
        switch (response.Status)
        {
            case GuardStatus.CancelledByUser:
                StopTimers();
                stateStore.Clear();
                pendingRequest?.TrySetResult(response);
                pendingRequest = null;
                window.HidePlaque();
                ScheduleShutdown();
                break;
            case GuardStatus.ConfirmStop:
                window.SetDisplayText(displayTexts.ConfirmStop);
                StartConfirmStopTimer();
                break;
            case GuardStatus.StoppedByUser:
                StopTimers();
                stateStore.WriteTerminalState(
                    response.Status,
                    response.Owner,
                    response.StartedAtUtc);
                window.HidePlaque();
                ScheduleShutdown();
                break;
        }
    }

    private void StartConfirmStopTimer()
    {
        confirmStopTimer?.Stop();
        confirmStopTimer = new DispatcherTimer(ConfirmStopTimeout, DispatcherPriority.Normal, (_, _) =>
        {
            confirmStopTimer?.Stop();
            confirmStopTimer = null;
            GuardResponse response = stateMachine.ConfirmStopTimedOut();
            if (response.Status == GuardStatus.Running)
            {
                window.SetDisplayText(displayTexts.Running);
            }
        }, dispatcher);
        confirmStopTimer.Start();
    }

    private void StartGuardLifetimeTimer()
    {
        guardLifetimeTimer?.Stop();
        guardLifetimeTimer = new DispatcherTimer(GuardLifetime, DispatcherPriority.Normal, (_, _) =>
        {
            HandleGuardLifetimeTimeout();
        }, dispatcher);
        guardLifetimeTimer.Start();
    }

    private void HandleGuardLifetimeTimeout()
    {
        StopTimers();
        GuardResponse response = stateMachine.CloseByTimeout();
        stateStore.WriteTerminalState(
            response.Status,
            response.Owner,
            response.StartedAtUtc);
        pendingRequest?.TrySetResult(response);
        pendingRequest = null;
        window.HidePlaque();
        ScheduleShutdown();
    }

    private void HandleCloseByUser()
    {
        StopTimers();
        GuardResponse response = stateMachine.CloseByUser();
        stateStore.WriteTerminalState(
            response.Status,
            response.Owner,
            response.StartedAtUtc);
        pendingRequest?.TrySetResult(response);
        pendingRequest = null;
        window.HidePlaque();
        ScheduleShutdown();
    }

    private void StopTimers()
    {
        countdownTimer?.Stop();
        countdownTimer = null;
        confirmStopTimer?.Stop();
        confirmStopTimer = null;
        guardLifetimeTimer?.Stop();
        guardLifetimeTimer = null;
    }

    private async void ScheduleShutdown()
    {
        if (shutdownScheduled)
        {
            return;
        }

        shutdownScheduled = true;
        await Task.Delay(ShutdownDelay);
        if (!dispatcher.HasShutdownStarted)
        {
            dispatcher.Invoke(() => Application.Current.Shutdown());
        }
    }
}
