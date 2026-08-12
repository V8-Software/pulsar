using TestGiveMeSpace.Core;

namespace TestGiveMeSpace.Tests;

public sealed class GuardStateMachineTests
{
    [Fact]
    public void Request_from_idle_starts_countdown()
    {
        var machine = new GuardStateMachine();

        var response = machine.Request();

        Assert.Equal(GuardStatus.Countdown, machine.State);
        Assert.Equal(GuardStatus.Countdown, response.Status);
        Assert.Equal(ExitCodes.Success, response.ExitCode);
    }

    [Fact]
    public void Request_records_owner_and_start_time()
    {
        var machine = new GuardStateMachine();
        DateTimeOffset startedAtUtc = new(2026, 5, 20, 1, 0, 0, TimeSpan.Zero);

        var response = machine.Request("chat-1", startedAtUtc);

        Assert.Equal(GuardStatus.Countdown, response.Status);
        Assert.Equal("chat-1", response.Owner);
        Assert.Equal(startedAtUtc, response.StartedAtUtc);
    }

    [Fact]
    public void Countdown_completed_grants_request_and_enters_running()
    {
        var machine = new GuardStateMachine();
        machine.Request();

        var response = machine.CompleteCountdown();

        Assert.Equal(GuardStatus.Running, machine.State);
        Assert.Equal(GuardStatus.Granted, response.Status);
        Assert.Equal(ExitCodes.Success, response.ExitCode);
    }

    [Fact]
    public void Click_during_countdown_cancels_current_request_without_terminal_state()
    {
        var machine = new GuardStateMachine();
        machine.Request();

        var response = machine.LeftClick();

        Assert.Equal(GuardStatus.Idle, machine.State);
        Assert.Equal(GuardStatus.CancelledByUser, response.Status);
        Assert.Equal(ExitCodes.CancelledByUser, response.ExitCode);
        Assert.False(response.IsTerminalState);
    }

    [Fact]
    public void First_click_during_running_enters_confirm_stop()
    {
        var machine = new GuardStateMachine();
        machine.Request();
        machine.CompleteCountdown();

        var response = machine.LeftClick();

        Assert.Equal(GuardStatus.ConfirmStop, machine.State);
        Assert.Equal(GuardStatus.ConfirmStop, response.Status);
        Assert.Equal(ExitCodes.Success, response.ExitCode);
    }

    [Fact]
    public void Second_click_during_confirm_stop_stops_and_persists_terminal_state()
    {
        var machine = new GuardStateMachine();
        machine.Request();
        machine.CompleteCountdown();
        machine.LeftClick();

        var response = machine.LeftClick();

        Assert.Equal(GuardStatus.StoppedByUser, machine.State);
        Assert.Equal(GuardStatus.StoppedByUser, response.Status);
        Assert.Equal(ExitCodes.StoppedByUser, response.ExitCode);
        Assert.True(response.IsTerminalState);
    }

    [Fact]
    public void Status_after_user_stop_keeps_owner_and_start_time()
    {
        var machine = new GuardStateMachine();
        DateTimeOffset startedAtUtc = new(2026, 5, 20, 1, 1, 0, TimeSpan.Zero);
        machine.Request("chat-1", startedAtUtc);
        machine.CompleteCountdown();
        machine.LeftClick();

        var stopped = machine.LeftClick();
        var status = machine.Status();

        Assert.Equal(GuardStatus.StoppedByUser, stopped.Status);
        Assert.Equal(GuardStatus.StoppedByUser, status.Status);
        Assert.Equal("chat-1", status.Owner);
        Assert.Equal(startedAtUtc, status.StartedAtUtc);
    }

    [Fact]
    public void Confirm_stop_timeout_returns_to_running()
    {
        var machine = new GuardStateMachine();
        machine.Request();
        machine.CompleteCountdown();
        machine.LeftClick();

        var response = machine.ConfirmStopTimedOut();

        Assert.Equal(GuardStatus.Running, machine.State);
        Assert.Equal(GuardStatus.Running, response.Status);
        Assert.Equal(ExitCodes.Success, response.ExitCode);
    }

    [Fact]
    public void Finish_after_one_minute_finishes_with_sound()
    {
        var machine = new GuardStateMachine();
        DateTimeOffset startedAtUtc = new(2026, 6, 13, 12, 0, 0, TimeSpan.Zero);
        machine.Request(requestStartedAtUtc: startedAtUtc);
        machine.CompleteCountdown();

        var response = machine.Finish(finishedAtUtc: startedAtUtc.AddMinutes(1));

        Assert.Equal(GuardStatus.Idle, machine.State);
        Assert.Equal(GuardStatus.Finished, response.Status);
        Assert.Equal(ExitCodes.Success, response.ExitCode);
        Assert.True(response.ShouldBeep);
    }

    [Fact]
    public void Finish_before_one_minute_finishes_without_sound()
    {
        var machine = new GuardStateMachine();
        DateTimeOffset startedAtUtc = new(2026, 6, 13, 12, 0, 0, TimeSpan.Zero);
        machine.Request(requestStartedAtUtc: startedAtUtc);
        machine.CompleteCountdown();

        var response = machine.Finish(finishedAtUtc: startedAtUtc.AddSeconds(59));

        Assert.Equal(GuardStatus.Idle, machine.State);
        Assert.Equal(GuardStatus.Finished, response.Status);
        Assert.Equal(ExitCodes.Success, response.ExitCode);
        Assert.False(response.ShouldBeep);
    }

    [Fact]
    public void Finish_from_different_owner_is_rejected()
    {
        var machine = new GuardStateMachine();
        DateTimeOffset startedAtUtc = new(2026, 5, 20, 1, 2, 0, TimeSpan.Zero);
        machine.Request("chat-1", startedAtUtc);
        machine.CompleteCountdown();

        var response = machine.Finish("chat-2");

        Assert.Equal(GuardStatus.OwnerMismatch, response.Status);
        Assert.Equal(GuardStatus.Running, machine.State);
        Assert.Equal("chat-1", response.Owner);
        Assert.Equal(startedAtUtc, response.StartedAtUtc);
    }

    [Fact]
    public void Active_session_rejects_avoid_commands_without_its_owner()
    {
        GuardStateMachine machine = new();
        machine.Request("chat-1");
        machine.CompleteCountdown();

        GuardResponse response = machine.ValidateRunningOwner(requestOwner: null)
            ?? throw new InvalidOperationException("Владелец активной сессии должен проверяться.");

        Assert.Equal(GuardStatus.OwnerMismatch, response.Status);
        Assert.Equal("chat-1", response.Owner);
        Assert.Equal(GuardStatus.Running, machine.State);
    }

    [Fact]
    public void Unowned_active_session_rejects_a_different_named_owner()
    {
        GuardStateMachine machine = new();
        machine.Request();
        machine.CompleteCountdown();

        GuardResponse response = machine.ValidateRunningOwner("chat-1")
            ?? throw new InvalidOperationException("Владелец активной сессии должен проверяться.");

        Assert.Equal(GuardStatus.OwnerMismatch, response.Status);
        Assert.Null(response.Owner);
        Assert.Equal(GuardStatus.Running, machine.State);
    }

    [Fact]
    public void Unowned_active_session_rejects_a_missing_owner()
    {
        GuardStateMachine machine = new();
        machine.Request();
        machine.CompleteCountdown();

        GuardResponse response = machine.ValidateRunningOwner(requestOwner: null)
            ?? throw new InvalidOperationException("Для перемещения требуется именованный владелец.");

        Assert.Equal(GuardStatus.OwnerMismatch, response.Status);
        Assert.Equal(GuardStatus.Running, machine.State);
    }

    [Fact]
    public void Countdown_rejects_a_foreign_owner_before_returning_its_state()
    {
        GuardStateMachine machine = new();
        machine.Request("chat-1");

        GuardResponse foreign = machine.ValidateRunningOwner("chat-2")
            ?? throw new InvalidOperationException("Чужой владелец должен быть отклонён.");
        GuardResponse current = machine.ValidateRunningOwner("chat-1")
            ?? throw new InvalidOperationException("До Running команда не должна выполняться.");

        Assert.Equal(GuardStatus.OwnerMismatch, foreign.Status);
        Assert.Equal(GuardStatus.Countdown, current.Status);
    }

    [Fact]
    public void Finish_from_same_owner_finishes()
    {
        var machine = new GuardStateMachine();
        DateTimeOffset startedAtUtc = new(2026, 5, 20, 1, 3, 0, TimeSpan.Zero);
        machine.Request("chat-1", startedAtUtc);
        machine.CompleteCountdown();

        var response = machine.Finish("chat-1");

        Assert.Equal(GuardStatus.Finished, response.Status);
        Assert.Equal(GuardStatus.Idle, machine.State);
        Assert.Equal("chat-1", response.Owner);
        Assert.Equal(startedAtUtc, response.StartedAtUtc);
    }

    [Fact]
    public void Finish_can_close_unowned_run()
    {
        var machine = new GuardStateMachine();
        machine.Request();
        machine.CompleteCountdown();

        var response = machine.Finish("chat-1");

        Assert.Equal(GuardStatus.Finished, response.Status);
        Assert.Equal(GuardStatus.Idle, machine.State);
        Assert.Null(response.Owner);
        Assert.NotNull(response.StartedAtUtc);
    }

    [Fact]
    public void Cancel_from_different_owner_is_rejected()
    {
        var machine = new GuardStateMachine();
        DateTimeOffset startedAtUtc = new(2026, 5, 20, 1, 4, 0, TimeSpan.Zero);
        machine.Request("chat-1", startedAtUtc);

        var response = machine.Cancel("chat-2");

        Assert.Equal(GuardStatus.OwnerMismatch, response.Status);
        Assert.Equal(GuardStatus.Countdown, machine.State);
        Assert.Equal("chat-1", response.Owner);
        Assert.Equal(startedAtUtc, response.StartedAtUtc);
    }

    [Fact]
    public void Cancel_can_close_unowned_run()
    {
        var machine = new GuardStateMachine();
        machine.Request();

        var response = machine.Cancel("chat-1");

        Assert.Equal(GuardStatus.Cancelled, response.Status);
        Assert.Equal(GuardStatus.Idle, machine.State);
        Assert.Null(response.Owner);
        Assert.NotNull(response.StartedAtUtc);
    }

    [Fact]
    public void Finish_during_countdown_cancels_without_sound()
    {
        var machine = new GuardStateMachine();
        machine.Request();

        var response = machine.Finish();

        Assert.Equal(GuardStatus.Idle, machine.State);
        Assert.Equal(GuardStatus.Cancelled, response.Status);
        Assert.Equal(ExitCodes.Success, response.ExitCode);
        Assert.False(response.ShouldBeep);
    }

    [Fact]
    public void Finish_after_stopped_by_user_returns_stopped_without_sound()
    {
        var machine = new GuardStateMachine();
        machine.Request();
        machine.CompleteCountdown();
        machine.LeftClick();
        machine.LeftClick();

        var response = machine.Finish();

        Assert.Equal(GuardStatus.StoppedByUser, machine.State);
        Assert.Equal(GuardStatus.StoppedByUser, response.Status);
        Assert.Equal(ExitCodes.StoppedByUser, response.ExitCode);
        Assert.False(response.ShouldBeep);
    }

    [Fact]
    public void Close_by_user_enters_closed_terminal_state()
    {
        var machine = new GuardStateMachine();
        machine.Request();
        machine.CompleteCountdown();

        var response = machine.CloseByUser();

        Assert.Equal(GuardStatus.ClosedByUser, machine.State);
        Assert.Equal(GuardStatus.ClosedByUser, response.Status);
        Assert.Equal(ExitCodes.ClosedByUser, response.ExitCode);
        Assert.True(response.IsTerminalState);
    }

    [Fact]
    public void Close_by_timeout_enters_timeout_terminal_state()
    {
        var machine = new GuardStateMachine();
        DateTimeOffset startedAtUtc = new(2026, 6, 13, 12, 0, 0, TimeSpan.Zero);
        machine.Request("chat-1", startedAtUtc);
        machine.CompleteCountdown();

        var response = machine.CloseByTimeout();

        Assert.Equal(GuardStatus.ClosedByTimeout, machine.State);
        Assert.Equal(GuardStatus.ClosedByTimeout, response.Status);
        Assert.Equal(ExitCodes.ClosedByTimeout, response.ExitCode);
        Assert.Equal("chat-1", response.Owner);
        Assert.Equal(startedAtUtc, response.StartedAtUtc);
        Assert.True(response.IsTerminalState);
    }

    [Fact]
    public void Request_while_running_returns_busy()
    {
        var machine = new GuardStateMachine();
        machine.Request();
        machine.CompleteCountdown();

        var response = machine.Request();

        Assert.Equal(GuardStatus.Running, machine.State);
        Assert.Equal(GuardStatus.BusyRunning, response.Status);
        Assert.Equal(ExitCodes.Busy, response.ExitCode);
    }
}
