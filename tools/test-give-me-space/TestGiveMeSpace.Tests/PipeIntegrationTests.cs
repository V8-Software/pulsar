using System.Collections.Concurrent;
using System.IO.Pipes;
using TestGiveMeSpace.Core;

namespace TestGiveMeSpace.Tests;

public sealed class PipeIntegrationTests
{
    [Fact]
    public async Task Client_sends_command_and_reads_response()
    {
        string pipeName = UniquePipeName();
        using CancellationTokenSource serverCancellation = new();
        RecordingHandler handler = new(_ => Task.FromResult(GuardResponse.FromStatus(GuardStatus.Running)));
        GuardPipeServer server = new(pipeName, handler);
        Task serverTask = server.RunAsync(serverCancellation.Token);

        GuardPipeClient client = new(pipeName, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        GuardResponse response = await client.SendAsync(GuardCommand.Status, CancellationToken.None);

        serverCancellation.Cancel();
        await serverTask.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(GuardStatus.Running, response.Status);
        Assert.Equal([GuardCommand.Status], handler.Commands);
    }

    [Fact]
    public async Task Client_sends_request_purpose()
    {
        string pipeName = UniquePipeName();
        using CancellationTokenSource serverCancellation = new();
        RecordingHandler handler = new(_ => Task.FromResult(GuardResponse.FromStatus(GuardStatus.Granted)));
        GuardPipeServer server = new(pipeName, handler);
        Task serverTask = server.RunAsync(serverCancellation.Token);

        GuardPipeClient client = new(pipeName, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        GuardResponse response = await client.SendAsync(
            GuardCommand.Request,
            GuardPurpose.ObserveWindows,
            CancellationToken.None);

        serverCancellation.Cancel();
        await serverTask.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(GuardStatus.Granted, response.Status);
        Assert.Equal([GuardPurpose.ObserveWindows], handler.Purposes);
    }

    [Fact]
    public async Task Client_sends_request_owner()
    {
        string pipeName = UniquePipeName();
        using CancellationTokenSource serverCancellation = new();
        RecordingHandler handler = new(_ => Task.FromResult(GuardResponse.FromStatus(GuardStatus.Granted)));
        GuardPipeServer server = new(pipeName, handler);
        Task serverTask = server.RunAsync(serverCancellation.Token);

        GuardPipeClient client = new(pipeName, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        GuardResponse response = await client.SendAsync(
            GuardCommand.Request,
            GuardPurpose.Test,
            "chat-1",
            CancellationToken.None);

        serverCancellation.Cancel();
        await serverTask.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(GuardStatus.Granted, response.Status);
        Assert.Equal("chat-1", Assert.Single(handler.Owners));
    }

    [Fact]
    public async Task Client_sends_avoid_point_coordinates_and_owner()
    {
        string pipeName = UniquePipeName();
        using CancellationTokenSource serverCancellation = new();
        RecordingHandler handler = new(_ => Task.FromResult(GuardResponse.FromStatus(GuardStatus.Granted)));
        GuardPipeServer server = new(pipeName, handler);
        Task serverTask = server.RunAsync(serverCancellation.Token);

        GuardPipeClient client = new(pipeName, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        GuardResponse response = await client.SendAsync(
            new GuardRequest(GuardCommand.AvoidPoint, GuardPurpose.Test, "chat-1", X: -120, Y: 340),
            CancellationToken.None);

        serverCancellation.Cancel();
        await serverTask.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(GuardStatus.Granted, response.Status);
        GuardRequest request = Assert.Single(handler.Requests);
        Assert.Equal(GuardCommand.AvoidPoint, request.Command);
        Assert.Equal("chat-1", request.Owner);
        Assert.Equal(-120, request.X);
        Assert.Equal(340, request.Y);
    }

    [Fact]
    public async Task Client_returns_server_not_ready_when_pipe_is_absent()
    {
        GuardPipeClient client = new(UniquePipeName(), TimeSpan.FromMilliseconds(50), TimeSpan.FromSeconds(1));

        GuardResponse response = await client.SendAsync(GuardCommand.Status, CancellationToken.None);

        Assert.Equal(GuardStatus.ServerNotReady, response.Status);
        Assert.Equal(ExitCodes.IpcError, response.ExitCode);
    }

    [Fact]
    public async Task Request_waits_for_user_decision_beyond_regular_read_timeout()
    {
        string pipeName = UniquePipeName();
        using CancellationTokenSource serverCancellation = new();
        RecordingHandler handler = new(async _ =>
        {
            await Task.Delay(120);
            return GuardResponse.FromStatus(GuardStatus.Granted);
        });
        GuardPipeServer server = new(pipeName, handler);
        Task serverTask = server.RunAsync(serverCancellation.Token);
        GuardPipeClient client = new(pipeName, TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(20));

        GuardResponse response = await client.SendAsync(GuardCommand.Request, CancellationToken.None);

        serverCancellation.Cancel();
        await serverTask.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(GuardStatus.Granted, response.Status);
    }

    [Fact]
    public async Task Request_waits_for_pipe_to_appear_within_request_timeout()
    {
        string pipeName = UniquePipeName();
        using CancellationTokenSource serverCancellation = new();
        RecordingHandler handler = new(_ => Task.FromResult(GuardResponse.FromStatus(GuardStatus.Granted)));
        GuardPipeClient client = new(
            pipeName,
            TimeSpan.FromMilliseconds(50),
            TimeSpan.FromSeconds(1),
            requestTimeout: TimeSpan.FromSeconds(1));

        Task<GuardResponse> responseTask = client.SendAsync(GuardCommand.Request, CancellationToken.None);
        await Task.Delay(150);

        GuardPipeServer server = new(pipeName, handler);
        Task serverTask = server.RunAsync(serverCancellation.Token);
        GuardResponse response = await responseTask.WaitAsync(TimeSpan.FromSeconds(2));

        serverCancellation.Cancel();
        await serverTask.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(GuardStatus.Granted, response.Status);
        Assert.Equal([GuardCommand.Request], handler.Commands);
    }

    [Fact]
    public async Task Request_retries_transient_io_failure_before_connecting()
    {
        string pipeName = UniquePipeName();
        using CancellationTokenSource serverCancellation = new();
        RecordingHandler handler = new(_ => Task.FromResult(GuardResponse.FromStatus(GuardStatus.Granted)));
        GuardPipeServer server = new(pipeName, handler);
        Task serverTask = server.RunAsync(serverCancellation.Token);
        int pipeCreationAttempts = 0;
        GuardPipeClient client = new(
            pipeName,
            TimeSpan.FromMilliseconds(50),
            TimeSpan.FromSeconds(1),
            requestTimeout: TimeSpan.FromSeconds(1),
            pipeFactory: () =>
            {
                pipeCreationAttempts++;
                if (pipeCreationAttempts == 1)
                {
                    throw new IOException("transient pipe setup failure");
                }

                return new NamedPipeClientStream(
                    ".",
                    pipeName,
                    PipeDirection.InOut,
                    PipeOptions.Asynchronous);
            });

        GuardResponse response = await client.SendAsync(GuardCommand.Request, CancellationToken.None);

        serverCancellation.Cancel();
        await serverTask.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(GuardStatus.Granted, response.Status);
        Assert.Equal(2, pipeCreationAttempts);
    }

    [Fact]
    public async Task Request_times_out_when_server_accepts_connection_but_does_not_answer()
    {
        string pipeName = UniquePipeName();
        using CancellationTokenSource serverCancellation = new();
        RecordingHandler handler = new(async _ =>
        {
            await Task.Delay(TimeSpan.FromSeconds(2));
            return GuardResponse.FromStatus(GuardStatus.Granted);
        });
        GuardPipeServer server = new(pipeName, handler);
        Task serverTask = server.RunAsync(serverCancellation.Token);
        GuardPipeClient client = new(
            pipeName,
            TimeSpan.FromSeconds(1),
            TimeSpan.FromMilliseconds(20),
            requestTimeout: TimeSpan.FromMilliseconds(150));

        GuardResponse response = await client.SendAsync(GuardCommand.Request, CancellationToken.None);

        serverCancellation.Cancel();
        await serverTask.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(GuardStatus.IpcError, response.Status);
        Assert.Equal(ExitCodes.IpcError, response.ExitCode);
        Assert.Contains("request", response.Message);
    }

    [Fact]
    public async Task Non_request_command_times_out_when_server_does_not_answer()
    {
        string pipeName = UniquePipeName();
        using CancellationTokenSource serverCancellation = new();
        RecordingHandler handler = new(async _ =>
        {
            await Task.Delay(120);
            return GuardResponse.FromStatus(GuardStatus.Running);
        });
        GuardPipeServer server = new(pipeName, handler);
        Task serverTask = server.RunAsync(serverCancellation.Token);
        GuardPipeClient client = new(pipeName, TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(20));

        GuardResponse response = await client.SendAsync(GuardCommand.Status, CancellationToken.None);

        serverCancellation.Cancel();
        await serverTask.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(GuardStatus.IpcError, response.Status);
    }

    [Fact]
    public async Task Parallel_requests_cannot_start_two_guarded_sections()
    {
        string pipeName = UniquePipeName();
        using CancellationTokenSource serverCancellation = new();
        BlockingRequestHandler handler = new();
        GuardPipeServer server = new(pipeName, handler);
        Task serverTask = server.RunAsync(serverCancellation.Token);
        GuardPipeClient client = new(pipeName, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));

        Task<GuardResponse> firstRequest = client.SendAsync(GuardCommand.Request, CancellationToken.None);
        await handler.FirstRequestReachedCountdown.Task.WaitAsync(TimeSpan.FromSeconds(2));

        GuardResponse secondResponse = await client.SendAsync(GuardCommand.Request, CancellationToken.None);
        handler.AllowFirstRequestToComplete.SetResult();
        GuardResponse firstResponse = await firstRequest.WaitAsync(TimeSpan.FromSeconds(2));

        serverCancellation.Cancel();
        await serverTask.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(GuardStatus.Granted, firstResponse.Status);
        Assert.Equal(GuardStatus.BusyCountdown, secondResponse.Status);
    }

    private static string UniquePipeName()
        => $"TestGiveMeSpace.Tests.{Guid.NewGuid():N}";

    private sealed class RecordingHandler(Func<GuardRequest, Task<GuardResponse>> handle) : IGuardCommandHandler
    {
        private readonly ConcurrentQueue<GuardRequest> requests = new();

        public GuardCommand[] Commands => requests.Select(request => request.Command).ToArray();
        public GuardPurpose[] Purposes => requests.Select(request => request.Purpose).ToArray();
        public string?[] Owners => requests.Select(request => request.Owner).ToArray();
        public GuardRequest[] Requests => requests.ToArray();

        public async Task<GuardResponse> HandleAsync(GuardRequest request, CancellationToken cancellationToken)
        {
            requests.Enqueue(request);
            return await handle(request);
        }
    }

    private sealed class BlockingRequestHandler : IGuardCommandHandler
    {
        private readonly object syncRoot = new();
        private readonly GuardStateMachine stateMachine = new();

        public TaskCompletionSource FirstRequestReachedCountdown { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource AllowFirstRequestToComplete { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<GuardResponse> HandleAsync(GuardRequest request, CancellationToken cancellationToken)
        {
            if (request.Command != GuardCommand.Request)
            {
                lock (syncRoot)
                {
                    return stateMachine.Status();
                }
            }

            GuardResponse requestResponse;
            lock (syncRoot)
            {
                requestResponse = stateMachine.Request();
                if (requestResponse.Status == GuardStatus.Countdown)
                {
                    FirstRequestReachedCountdown.TrySetResult();
                }
            }

            if (requestResponse.Status != GuardStatus.Countdown)
            {
                return requestResponse;
            }

            await AllowFirstRequestToComplete.Task.WaitAsync(cancellationToken);
            lock (syncRoot)
            {
                return stateMachine.CompleteCountdown();
            }
        }
    }
}
