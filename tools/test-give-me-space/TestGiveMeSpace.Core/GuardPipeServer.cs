using System.IO.Pipes;
using System.Text;

namespace TestGiveMeSpace.Core;

public sealed class GuardPipeServer(
    string pipeName,
    IGuardCommandHandler handler,
    TimeSpan? ioTimeout = null)
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);
    private readonly TimeSpan timeout = ioTimeout ?? TimeSpan.FromSeconds(5);

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            NamedPipeServerStream pipe = CreatePipe();
            using CancellationTokenRegistration registration = cancellationToken.Register(pipe.Dispose);
            try
            {
                await pipe.WaitForConnectionAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                await pipe.DisposeAsync();
                break;
            }
            catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            _ = HandleConnectedClientAsync(pipe, cancellationToken);
        }
    }

    private NamedPipeServerStream CreatePipe()
        => new(
            pipeName,
            PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);

    private async Task HandleConnectedClientAsync(
        NamedPipeServerStream pipe,
        CancellationToken cancellationToken)
    {
        await using (pipe)
        {
            GuardResponse response;
            try
            {
                using StreamReader reader = new(pipe, Utf8NoBom, leaveOpen: true);
                using StreamWriter writer = new(pipe, Utf8NoBom, leaveOpen: true) { AutoFlush = true };

                string? requestLine = await reader.ReadLineAsync()
                    .WaitAsync(timeout, cancellationToken);

                response = GuardProtocol.TryParseRequest(requestLine, out GuardRequest request)
                    ? await handler.HandleAsync(request, cancellationToken)
                    : GuardResponse.FromStatus(GuardStatus.ProtocolError);

                await writer.WriteLineAsync(response.ToJson())
                    .WaitAsync(timeout, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception)
            {
                if (pipe.IsConnected)
                {
                    try
                    {
                        using StreamWriter writer = new(pipe, Utf8NoBom, leaveOpen: true) { AutoFlush = true };
                        await writer.WriteLineAsync(GuardResponse.FromStatus(GuardStatus.IpcError).ToJson())
                            .WaitAsync(timeout, CancellationToken.None);
                    }
                    catch
                    {
                    }
                }
            }
        }
    }
}
