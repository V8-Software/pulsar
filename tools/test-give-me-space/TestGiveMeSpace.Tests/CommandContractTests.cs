using System.Text.Json;
using TestGiveMeSpace.Core;

namespace TestGiveMeSpace.Tests;

public sealed class CommandContractTests
{
    [Theory]
    [InlineData(GuardStatus.Granted, "granted", 0)]
    [InlineData(GuardStatus.CancelledByUser, "cancelled_by_user", 20)]
    [InlineData(GuardStatus.StoppedByUser, "stopped_by_user", 21)]
    [InlineData(GuardStatus.BusyRunning, "busy_running", 22)]
    [InlineData(GuardStatus.ClosedByUser, "closed_by_user", 23)]
    [InlineData(GuardStatus.ClosedByTimeout, "closed_by_timeout", 24)]
    [InlineData(GuardStatus.OwnerMismatch, "owner_mismatch", 22)]
    [InlineData(GuardStatus.IpcError, "ipc_error", 50)]
    [InlineData(GuardStatus.ServerNotReady, "server_not_ready", 50)]
    [InlineData(GuardStatus.ProtocolError, "protocol_error", 51)]
    [InlineData(GuardStatus.IoError, "io_error", 52)]
    public void Response_json_contains_status_and_exit_code(GuardStatus status, string expectedStatus, int expectedExitCode)
    {
        var response = GuardResponse.FromStatus(status);

        using var doc = JsonDocument.Parse(response.ToJson());

        Assert.Equal(expectedStatus, doc.RootElement.GetProperty("status").GetString());
        Assert.Equal(expectedExitCode, response.ExitCode);
    }

    [Fact]
    public void Technical_errors_are_not_user_cancellations()
    {
        var technical = new[]
        {
            GuardResponse.FromStatus(GuardStatus.IpcError),
            GuardResponse.FromStatus(GuardStatus.ServerNotReady),
            GuardResponse.FromStatus(GuardStatus.ProtocolError),
            GuardResponse.FromStatus(GuardStatus.IoError),
        };

        Assert.All(technical, response => Assert.InRange(response.ExitCode, 50, 59));
    }
}
