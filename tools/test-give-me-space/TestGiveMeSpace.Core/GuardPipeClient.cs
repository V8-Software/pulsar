using System.IO.Pipes;
using System.Text;

namespace TestGiveMeSpace.Core;

public sealed class GuardPipeClient(
    string pipeName,
    TimeSpan connectTimeout,
    TimeSpan readTimeout,
    TimeSpan? requestTimeout = null,
    Func<NamedPipeClientStream>? pipeFactory = null) : IGuardPipeClient
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);
    private static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan RequestConnectRetryDelay = TimeSpan.FromMilliseconds(100);
    private readonly TimeSpan requestTimeoutValue = requestTimeout ?? DefaultRequestTimeout;
    private readonly Func<NamedPipeClientStream>? customPipeFactory = pipeFactory;

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
        => await SendAsync(new GuardRequest(command, purpose, owner), cancellationToken);

    public async Task<GuardResponse> SendAsync(GuardRequest request, CancellationToken cancellationToken)
    {
        GuardCommand command = request.Command;
        DateTimeOffset requestDeadline = DateTimeOffset.UtcNow.Add(requestTimeoutValue);

        await using NamedPipeClientStream? pipe = command == GuardCommand.Request
            ? await ConnectRequestPipeAsync(requestDeadline, cancellationToken)
            : await ConnectPipeAsync(cancellationToken);
        if (pipe is null)
        {
            return command == GuardCommand.Request
                ? GuardResponse.FromStatus(GuardStatus.IpcError, BuildRequestTimeoutMessage())
                : GuardResponse.FromStatus(GuardStatus.ServerNotReady);
        }

        try
        {
            using StreamWriter writer = new(pipe, Utf8NoBom, leaveOpen: true) { AutoFlush = true };
            using StreamReader reader = new(pipe, Utf8NoBom, leaveOpen: true);

            await writer.WriteLineAsync(GuardProtocol.SerializeRequest(request))
                .WaitAsync(GetOperationTimeout(command, requestDeadline), cancellationToken);

            string? responseLine = command == GuardCommand.Request
                ? await reader.ReadLineAsync().WaitAsync(GetOperationTimeout(command, requestDeadline), cancellationToken)
                : await reader.ReadLineAsync().WaitAsync(readTimeout, cancellationToken);

            return GuardProtocol.ParseResponse(responseLine);
        }
        catch (TimeoutException)
        {
            return command == GuardCommand.Request
                ? GuardResponse.FromStatus(GuardStatus.IpcError, BuildRequestTimeoutMessage())
                : GuardResponse.FromStatus(GuardStatus.IpcError);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (IOException)
        {
            return GuardResponse.FromStatus(GuardStatus.IpcError);
        }
        catch (UnauthorizedAccessException)
        {
            return GuardResponse.FromStatus(GuardStatus.IpcError);
        }
    }

    private async Task<NamedPipeClientStream?> ConnectPipeAsync(CancellationToken cancellationToken)
    {
        NamedPipeClientStream? pipe = null;
        try
        {
            pipe = CreatePipe();
            await pipe.ConnectAsync(ToTimeoutMilliseconds(connectTimeout), cancellationToken);
            return pipe;
        }
        catch (TimeoutException)
        {
            if (pipe is not null)
            {
                await pipe.DisposeAsync();
            }

            return null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (pipe is not null)
            {
                await pipe.DisposeAsync();
            }

            throw;
        }
        catch (IOException)
        {
            if (pipe is not null)
            {
                await pipe.DisposeAsync();
            }

            return null;
        }
        catch (UnauthorizedAccessException)
        {
            if (pipe is not null)
            {
                await pipe.DisposeAsync();
            }

            return null;
        }
    }

    private async Task<NamedPipeClientStream?> ConnectRequestPipeAsync(
        DateTimeOffset deadline,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            TimeSpan remaining = Remaining(deadline);
            if (remaining <= TimeSpan.Zero)
            {
                return null;
            }

            NamedPipeClientStream? pipe = null;
            try
            {
                pipe = CreatePipe();
                TimeSpan attemptTimeout = Min(connectTimeout, remaining);
                await pipe.ConnectAsync(ToTimeoutMilliseconds(attemptTimeout), cancellationToken);
                return pipe;
            }
            catch (TimeoutException)
            {
                if (pipe is not null)
                {
                    await pipe.DisposeAsync();
                }

                if (!await WaitBeforeRequestConnectRetryAsync(deadline, cancellationToken))
                {
                    return null;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                if (pipe is not null)
                {
                    await pipe.DisposeAsync();
                }

                throw;
            }
            catch (IOException)
            {
                if (pipe is not null)
                {
                    await pipe.DisposeAsync();
                }

                if (!await WaitBeforeRequestConnectRetryAsync(deadline, cancellationToken))
                {
                    return null;
                }
            }
            catch (UnauthorizedAccessException)
            {
                if (pipe is not null)
                {
                    await pipe.DisposeAsync();
                }

                return null;
            }
        }
    }

    private NamedPipeClientStream CreatePipe()
        => customPipeFactory?.Invoke()
            ?? new(
            ".",
            pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);

    private static async Task<bool> WaitBeforeRequestConnectRetryAsync(
        DateTimeOffset deadline,
        CancellationToken cancellationToken)
    {
        TimeSpan retryDelay = Min(RequestConnectRetryDelay, Remaining(deadline));
        if (retryDelay <= TimeSpan.Zero)
        {
            return false;
        }

        await Task.Delay(retryDelay, cancellationToken);
        return true;
    }

    private TimeSpan GetOperationTimeout(GuardCommand command, DateTimeOffset requestDeadline)
        => command == GuardCommand.Request
            ? PositiveRemaining(requestDeadline)
            : readTimeout;

    private static TimeSpan Remaining(DateTimeOffset deadline)
        => deadline - DateTimeOffset.UtcNow;

    private static TimeSpan PositiveRemaining(DateTimeOffset deadline)
    {
        TimeSpan remaining = Remaining(deadline);
        return remaining <= TimeSpan.Zero
            ? TimeSpan.FromMilliseconds(1)
            : remaining;
    }

    private static TimeSpan Min(TimeSpan first, TimeSpan second)
        => first <= second ? first : second;

    private string BuildRequestTimeoutMessage()
        => $"Сервер test-give-me-space не ответил на request за {ToTimeoutSeconds(requestTimeoutValue)} секунд";

    private static int ToTimeoutSeconds(TimeSpan timeout)
        => Math.Max(1, (int)Math.Ceiling(timeout.TotalSeconds));

    private static int ToTimeoutMilliseconds(TimeSpan timeout)
        => Math.Max(1, (int)Math.Ceiling(timeout.TotalMilliseconds));
}
