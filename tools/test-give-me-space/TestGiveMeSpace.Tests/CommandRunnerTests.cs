using TestGiveMeSpace.Core;

namespace TestGiveMeSpace.Tests;

public sealed class CommandRunnerTests
{
    [Fact]
    public async Task Status_without_server_returns_terminal_state_from_store()
    {
        string statePath = TempFilePath();
        StateStore store = new(statePath);
        store.WriteTerminalState(GuardStatus.StoppedByUser);
        FakePipeClient client = new(_ => Task.FromResult(GuardResponse.FromStatus(GuardStatus.ServerNotReady)));
        FakeServerProcess serverProcess = new(isRunning: false);
        GuardCommandRunner runner = new(client, serverProcess, store);

        GuardResponse response = await runner.ExecuteAsync(GuardCommand.Status, CancellationToken.None);

        Assert.Equal(GuardStatus.StoppedByUser, response.Status);
        Assert.Equal(ExitCodes.StoppedByUser, response.ExitCode);
        Assert.False(serverProcess.WasStarted);
        Assert.Empty(client.Commands);
    }

    [Fact]
    public async Task Request_starts_server_clears_terminal_state_and_uses_pipe_response()
    {
        string statePath = TempFilePath();
        StateStore store = new(statePath);
        store.WriteTerminalState(GuardStatus.ClosedByUser);
        FakePipeClient client = new(_ => Task.FromResult(GuardResponse.FromStatus(GuardStatus.Granted)));
        FakeServerProcess serverProcess = new(isRunning: false);
        GuardCommandRunner runner = new(client, serverProcess, store);

        GuardResponse response = await runner.ExecuteAsync(GuardCommand.Request, CancellationToken.None);

        Assert.Equal(GuardStatus.Granted, response.Status);
        Assert.Equal(GuardStatus.Idle, store.ReadTerminalState());
        Assert.True(serverProcess.WasStarted);
        Assert.Equal([GuardCommand.Request], client.Commands);
        Assert.Equal([GuardPurpose.Test], client.Purposes);
    }

    [Fact]
    public async Task Request_passes_observe_windows_purpose_to_pipe()
    {
        StateStore store = new(TempFilePath());
        FakePipeClient client = new(_ => Task.FromResult(GuardResponse.FromStatus(GuardStatus.Granted)));
        FakeServerProcess serverProcess = new(isRunning: true);
        GuardCommandRunner runner = new(client, serverProcess, store);

        GuardResponse response = await runner.ExecuteAsync(
            GuardCommand.Request,
            CancellationToken.None,
            GuardPurpose.ObserveWindows);

        Assert.Equal(GuardStatus.Granted, response.Status);
        Assert.Equal([GuardPurpose.ObserveWindows], client.Purposes);
    }

    [Fact]
    public async Task Request_passes_owner_to_pipe()
    {
        StateStore store = new(TempFilePath());
        FakePipeClient client = new(_ => Task.FromResult(GuardResponse.FromStatus(GuardStatus.Granted)));
        FakeServerProcess serverProcess = new(isRunning: true);
        GuardCommandRunner runner = new(client, serverProcess, store);

        GuardResponse response = await runner.ExecuteAsync(
            GuardCommand.Request,
            CancellationToken.None,
            GuardPurpose.ObserveWindows,
            owner: "chat-1");

        Assert.Equal(GuardStatus.Granted, response.Status);
        Assert.Equal("chat-1", Assert.Single(client.Owners));
    }

    [Fact]
    public async Task Avoid_point_passes_coordinates_and_owner_to_pipe()
    {
        StateStore store = new(TempFilePath());
        FakePipeClient client = new(_ => Task.FromResult(GuardResponse.FromStatus(GuardStatus.Granted)));
        GuardCommandRunner runner = new(client, new FakeServerProcess(isRunning: true), store);

        GuardResponse response = await runner.ExecuteAsync(
            new GuardRequest(GuardCommand.AvoidPoint, GuardPurpose.Test, "chat-1", X: -120, Y: 340),
            CancellationToken.None);

        Assert.Equal(GuardStatus.Granted, response.Status);
        GuardRequest request = Assert.Single(client.Requests);
        Assert.Equal(GuardCommand.AvoidPoint, request.Command);
        Assert.Equal("chat-1", request.Owner);
        Assert.Equal(-120, request.X);
        Assert.Equal(340, request.Y);
    }

    [Fact]
    public async Task Avoid_point_with_incomplete_coordinates_is_rejected_without_sending_restore()
    {
        StateStore store = new(TempFilePath());
        FakePipeClient client = new(_ => Task.FromResult(GuardResponse.FromStatus(GuardStatus.Granted)));
        GuardCommandRunner runner = new(client, new FakeServerProcess(isRunning: true), store);

        GuardResponse response = await runner.ExecuteAsync(
            new GuardRequest(GuardCommand.AvoidPoint, GuardPurpose.Test, "chat-1", X: -120, Y: null),
            CancellationToken.None);

        Assert.Equal(GuardStatus.ProtocolError, response.Status);
        Assert.Empty(client.Requests);
    }

    [Fact]
    public async Task Restore_position_with_coordinates_is_rejected_without_sending_a_command()
    {
        StateStore store = new(TempFilePath());
        FakePipeClient client = new(_ => Task.FromResult(GuardResponse.FromStatus(GuardStatus.Granted)));
        GuardCommandRunner runner = new(client, new FakeServerProcess(isRunning: true), store);

        GuardResponse response = await runner.ExecuteAsync(
            new GuardRequest(GuardCommand.RestorePosition, GuardPurpose.Test, "chat-1", X: 10, Y: null),
            CancellationToken.None);

        Assert.Equal(GuardStatus.ProtocolError, response.Status);
        Assert.Empty(client.Requests);
    }

    [Fact]
    public async Task Status_without_server_returns_terminal_state_metadata_from_store()
    {
        string statePath = TempFilePath();
        StateStore store = new(statePath);
        DateTimeOffset startedAtUtc = new(2026, 5, 20, 0, 46, 18, TimeSpan.Zero);
        store.WriteTerminalState(
            GuardStatus.ClosedByUser,
            owner: "chat-1",
            startedAtUtc: startedAtUtc);
        FakePipeClient client = new(_ => Task.FromResult(GuardResponse.FromStatus(GuardStatus.ServerNotReady)));
        FakeServerProcess serverProcess = new(isRunning: false);
        GuardCommandRunner runner = new(client, serverProcess, store);

        GuardResponse response = await runner.ExecuteAsync(GuardCommand.Status, CancellationToken.None);

        Assert.Equal(GuardStatus.ClosedByUser, response.Status);
        Assert.Equal("chat-1", response.Owner);
        Assert.Equal(startedAtUtc, response.StartedAtUtc);
    }

    [Fact]
    public async Task Request_keeps_terminal_state_when_server_is_not_ready()
    {
        string statePath = TempFilePath();
        StateStore store = new(statePath);
        store.WriteTerminalState(GuardStatus.ClosedByUser);
        FakePipeClient client = new(_ => Task.FromResult(GuardResponse.FromStatus(GuardStatus.ServerNotReady)));
        FakeServerProcess serverProcess = new(isRunning: false);
        GuardCommandRunner runner = new(client, serverProcess, store);

        GuardResponse response = await runner.ExecuteAsync(GuardCommand.Request, CancellationToken.None);

        Assert.Equal(GuardStatus.ServerNotReady, response.Status);
        Assert.Equal(GuardStatus.ClosedByUser, store.ReadTerminalState());
        Assert.True(serverProcess.WasStarted);
    }

    [Fact]
    public async Task Request_returns_ipc_error_when_server_start_fails()
    {
        StateStore store = new(TempFilePath());
        FakePipeClient client = new(_ => Task.FromResult(GuardResponse.FromStatus(GuardStatus.Granted)));
        FakeServerProcess serverProcess = new(isRunning: false)
        {
            StartException = new InvalidOperationException("launcher failed"),
        };
        GuardCommandRunner runner = new(client, serverProcess, store);

        GuardResponse response = await runner.ExecuteAsync(GuardCommand.Request, CancellationToken.None);

        Assert.Equal(GuardStatus.IpcError, response.Status);
        Assert.Equal(ExitCodes.IpcError, response.ExitCode);
        Assert.Empty(client.Commands);
    }

    [Fact]
    public async Task Request_does_not_start_duplicate_server_when_one_is_running()
    {
        StateStore store = new(TempFilePath());
        FakePipeClient client = new(_ => Task.FromResult(GuardResponse.FromStatus(GuardStatus.BusyRunning)));
        FakeServerProcess serverProcess = new(isRunning: true);
        GuardCommandRunner runner = new(client, serverProcess, store);

        GuardResponse response = await runner.ExecuteAsync(GuardCommand.Request, CancellationToken.None);

        Assert.Equal(GuardStatus.BusyRunning, response.Status);
        Assert.False(serverProcess.WasStarted);
        Assert.Equal([GuardCommand.Request], client.Commands);
    }

    [Fact]
    public async Task Cancel_without_server_clears_terminal_state_and_returns_cancelled()
    {
        string statePath = TempFilePath();
        StateStore store = new(statePath);
        store.WriteTerminalState(GuardStatus.StoppedByUser);
        FakePipeClient client = new(_ => Task.FromResult(GuardResponse.FromStatus(GuardStatus.ServerNotReady)));
        GuardCommandRunner runner = new(client, new FakeServerProcess(isRunning: false), store);

        GuardResponse response = await runner.ExecuteAsync(GuardCommand.Cancel, CancellationToken.None);

        Assert.Equal(GuardStatus.Cancelled, response.Status);
        Assert.Equal(GuardStatus.Idle, store.ReadTerminalState());
    }

    [Fact]
    public async Task Finish_without_server_returns_terminal_state_without_sound()
    {
        string statePath = TempFilePath();
        StateStore store = new(statePath);
        store.WriteTerminalState(GuardStatus.ClosedByUser);
        FakePipeClient client = new(_ => Task.FromResult(GuardResponse.FromStatus(GuardStatus.ServerNotReady)));
        GuardCommandRunner runner = new(client, new FakeServerProcess(isRunning: false), store);

        GuardResponse response = await runner.ExecuteAsync(GuardCommand.Finish, CancellationToken.None);

        Assert.Equal(GuardStatus.ClosedByUser, response.Status);
        Assert.Equal(ExitCodes.ClosedByUser, response.ExitCode);
        Assert.False(response.ShouldBeep);
    }

    [Fact]
    public async Task Finish_without_server_returns_timeout_terminal_state_without_sound()
    {
        string statePath = TempFilePath();
        StateStore store = new(statePath);
        store.WriteTerminalState(GuardStatus.ClosedByTimeout);
        FakePipeClient client = new(_ => Task.FromResult(GuardResponse.FromStatus(GuardStatus.ServerNotReady)));
        GuardCommandRunner runner = new(client, new FakeServerProcess(isRunning: false), store);

        GuardResponse response = await runner.ExecuteAsync(GuardCommand.Finish, CancellationToken.None);

        Assert.Equal(GuardStatus.ClosedByTimeout, response.Status);
        Assert.Equal(ExitCodes.ClosedByTimeout, response.ExitCode);
        Assert.False(response.ShouldBeep);
    }

    [Fact]
    public async Task Finish_passes_owner_to_pipe()
    {
        StateStore store = new(TempFilePath());
        FakePipeClient client = new(_ => Task.FromResult(GuardResponse.FromStatus(GuardStatus.Finished)));
        GuardCommandRunner runner = new(client, new FakeServerProcess(isRunning: true), store);

        GuardResponse response = await runner.ExecuteAsync(
            GuardCommand.Finish,
            CancellationToken.None,
            owner: "chat-1");

        Assert.Equal(GuardStatus.Finished, response.Status);
        Assert.Equal("chat-1", Assert.Single(client.Owners));
    }

    [Fact]
    public async Task Finish_waits_for_server_shutdown_after_finished_response()
    {
        StateStore store = new(TempFilePath());
        FakePipeClient client = new(_ => Task.FromResult(GuardResponse.FromStatus(GuardStatus.Finished)));
        FakeServerProcess serverProcess = new(isRunning: true)
        {
            WaitUntilStoppedResult = false,
        };
        GuardCommandRunner runner = new(client, serverProcess, store);

        GuardResponse response = await runner.ExecuteAsync(GuardCommand.Finish, CancellationToken.None);

        Assert.Equal(GuardStatus.Finished, response.Status);
        TimeSpan waitTimeout = Assert.Single(serverProcess.WaitUntilStoppedTimeouts);
        Assert.InRange(waitTimeout, TimeSpan.FromMilliseconds(1), TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task Finish_returns_finished_when_post_response_shutdown_wait_is_cancelled()
    {
        StateStore store = new(TempFilePath());
        FakePipeClient client = new(_ => Task.FromResult(GuardResponse.FromStatus(GuardStatus.Finished)));
        FakeServerProcess serverProcess = new(isRunning: true)
        {
            WaitUntilStoppedException = new OperationCanceledException(),
        };
        GuardCommandRunner runner = new(client, serverProcess, store);

        GuardResponse response = await runner.ExecuteAsync(GuardCommand.Finish, CancellationToken.None);

        Assert.Equal(GuardStatus.Finished, response.Status);
        Assert.Single(serverProcess.WaitUntilStoppedTimeouts);
    }

    [Fact]
    public async Task Cancel_passes_owner_to_pipe()
    {
        StateStore store = new(TempFilePath());
        FakePipeClient client = new(_ => Task.FromResult(GuardResponse.FromStatus(GuardStatus.Cancelled)));
        GuardCommandRunner runner = new(client, new FakeServerProcess(isRunning: true), store);

        GuardResponse response = await runner.ExecuteAsync(
            GuardCommand.Cancel,
            CancellationToken.None,
            owner: "chat-1");

        Assert.Equal(GuardStatus.Cancelled, response.Status);
        Assert.Equal("chat-1", Assert.Single(client.Owners));
    }

    [Fact]
    public async Task Status_with_running_server_uses_pipe()
    {
        StateStore store = new(TempFilePath());
        FakePipeClient client = new(_ => Task.FromResult(GuardResponse.FromStatus(GuardStatus.Running)));
        GuardCommandRunner runner = new(client, new FakeServerProcess(isRunning: true), store);

        GuardResponse response = await runner.ExecuteAsync(GuardCommand.Status, CancellationToken.None);

        Assert.Equal(GuardStatus.Running, response.Status);
        Assert.Equal([GuardCommand.Status], client.Commands);
    }

    [Fact]
    public async Task Status_retries_transient_server_not_ready_while_mutex_exists()
    {
        StateStore store = new(TempFilePath());
        int calls = 0;
        FakePipeClient client = new(_ =>
        {
            calls++;
            return Task.FromResult(calls == 1
                ? GuardResponse.FromStatus(GuardStatus.ServerNotReady)
                : GuardResponse.FromStatus(GuardStatus.Running));
        });
        GuardCommandRunner runner = new(client, new FakeServerProcess(isRunning: true), store);

        GuardResponse response = await runner.ExecuteAsync(GuardCommand.Status, CancellationToken.None);

        Assert.Equal(GuardStatus.Running, response.Status);
        Assert.Equal([GuardCommand.Status, GuardCommand.Status], client.Commands);
    }

    [Fact]
    public async Task Status_returns_terminal_state_when_server_stops_after_pipe_failure()
    {
        StateStore store = new(TempFilePath());
        FakePipeClient client = new(_ => Task.FromResult(GuardResponse.FromStatus(GuardStatus.IpcError)));
        FakeServerProcess serverProcess = new(new[] { true, false });
        GuardCommandRunner runner = new(client, serverProcess, store);

        GuardResponse response = await runner.ExecuteAsync(GuardCommand.Status, CancellationToken.None);

        Assert.Equal(GuardStatus.Idle, response.Status);
    }

    [Fact]
    public async Task Status_retry_does_not_wait_for_regular_pipe_read_timeout()
    {
        StateStore store = new(TempFilePath());
        int calls = 0;
        FakePipeClient client = new(async _ =>
        {
            calls++;
            if (calls == 1)
            {
                return GuardResponse.FromStatus(GuardStatus.ServerNotReady);
            }

            await Task.Delay(TimeSpan.FromSeconds(3));
            return GuardResponse.FromStatus(GuardStatus.Running);
        });
        GuardCommandRunner runner = new(client, new FakeServerProcess(isRunning: true), store);

        DateTimeOffset startedAt = DateTimeOffset.UtcNow;
        GuardResponse response = await runner.ExecuteAsync(GuardCommand.Status, CancellationToken.None);
        TimeSpan elapsed = DateTimeOffset.UtcNow - startedAt;

        Assert.Equal(GuardStatus.IpcError, response.Status);
        Assert.True(elapsed < TimeSpan.FromMilliseconds(2500), $"elapsed={elapsed}");
    }

    private static string TempFilePath()
        => Path.Combine(Path.GetTempPath(), "TestGiveMeSpace.Tests", $"{Guid.NewGuid():N}.json");

    private sealed class FakePipeClient(Func<GuardCommand, Task<GuardResponse>> send) : IGuardPipeClient
    {
        private readonly List<GuardCommand> commands = [];
        private readonly List<GuardPurpose> purposes = [];
        private readonly List<string?> owners = [];
        private readonly List<GuardRequest> requests = [];

        public GuardCommand[] Commands => commands.ToArray();
        public GuardPurpose[] Purposes => purposes.ToArray();
        public string?[] Owners => owners.ToArray();
        public GuardRequest[] Requests => requests.ToArray();

        public async Task<GuardResponse> SendAsync(GuardCommand command, CancellationToken cancellationToken)
            => await SendAsync(command, GuardPurpose.Test, cancellationToken);

        public async Task<GuardResponse> SendAsync(
            GuardCommand command,
            GuardPurpose purpose,
            CancellationToken cancellationToken)
            => await SendAsync(command, purpose, owner: null, cancellationToken);

        public async Task<GuardResponse> SendAsync(
            GuardCommand command,
            GuardPurpose purpose,
            string? owner,
            CancellationToken cancellationToken)
        {
            commands.Add(command);
            purposes.Add(purpose);
            owners.Add(owner);
            return await send(command);
        }

        public async Task<GuardResponse> SendAsync(GuardRequest request, CancellationToken cancellationToken)
        {
            requests.Add(request);
            commands.Add(request.Command);
            purposes.Add(request.Purpose);
            owners.Add(request.Owner);
            return await send(request.Command);
        }
    }

    private sealed class FakeServerProcess : IGuardServerProcess
    {
        private readonly bool isRunning;
        private readonly Queue<bool>? isRunningSequence;

        public FakeServerProcess(bool isRunning)
        {
            this.isRunning = isRunning;
        }

        public FakeServerProcess(IEnumerable<bool> isRunningSequence)
        {
            this.isRunningSequence = new Queue<bool>(isRunningSequence);
        }

        public bool WasStarted { get; private set; }
        public Exception? StartException { get; init; }
        public bool WaitUntilStoppedResult { get; init; } = true;
        public Exception? WaitUntilStoppedException { get; init; }
        public List<TimeSpan> WaitUntilStoppedTimeouts { get; } = [];

        public bool IsRunning()
        {
            if (isRunningSequence is not null)
            {
                return isRunningSequence.Count > 0 && isRunningSequence.Dequeue();
            }

            return isRunning || WasStarted;
        }

        public void EnsureStarted()
        {
            WasStarted = true;
            if (StartException is not null)
            {
                throw StartException;
            }
        }

        public Task<bool> WaitUntilStoppedAsync(TimeSpan timeout, CancellationToken cancellationToken)
        {
            WaitUntilStoppedTimeouts.Add(timeout);
            if (WaitUntilStoppedException is not null)
            {
                throw WaitUntilStoppedException;
            }

            return Task.FromResult(WaitUntilStoppedResult);
        }
    }
}
