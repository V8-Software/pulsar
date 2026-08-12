using TestGiveMeSpace.Core;

namespace TestGiveMeSpace.Tests;

public sealed class GuardProtocolTests
{
    [Fact]
    public void Command_round_trips_as_json()
    {
        string json = GuardProtocol.SerializeCommand(GuardCommand.Request);

        Assert.True(GuardProtocol.TryParseCommand(json, out GuardCommand command));
        Assert.Equal(GuardCommand.Request, command);
    }

    [Fact]
    public void Request_round_trips_with_observe_windows_purpose()
    {
        string json = GuardProtocol.SerializeCommand(GuardCommand.Request, GuardPurpose.ObserveWindows);

        Assert.True(GuardProtocol.TryParseRequest(json, out GuardRequest request));
        Assert.Equal(GuardCommand.Request, request.Command);
        Assert.Equal(GuardPurpose.ObserveWindows, request.Purpose);
    }

    [Fact]
    public void Request_round_trips_with_owner()
    {
        string json = GuardProtocol.SerializeCommand(
            GuardCommand.Request,
            GuardPurpose.ObserveWindows,
            "019e02f6-8b5d-7431-bf62-77462c424b2a");

        Assert.True(GuardProtocol.TryParseRequest(json, out GuardRequest request));
        Assert.Equal("019e02f6-8b5d-7431-bf62-77462c424b2a", request.Owner);
    }

    [Fact]
    public void Avoid_point_round_trips_with_coordinates_and_owner()
    {
        string json = GuardProtocol.SerializeRequest(new GuardRequest(
            GuardCommand.AvoidPoint,
            GuardPurpose.Test,
            "chat-1",
            X: -120,
            Y: 340));

        Assert.True(GuardProtocol.TryParseRequest(json, out GuardRequest request));
        Assert.Equal(GuardCommand.AvoidPoint, request.Command);
        Assert.Equal(-120, request.X);
        Assert.Equal(340, request.Y);
        Assert.Equal("chat-1", request.Owner);
    }

    [Fact]
    public void Avoid_point_without_both_coordinates_is_rejected()
    {
        const string json = """{"command":"avoid-point","purpose":"test","owner":"chat-1","x":-120}""";

        Assert.False(GuardProtocol.TryParseRequest(json, out _));
    }

    [Fact]
    public void Command_without_purpose_uses_test_purpose()
    {
        string json = GuardProtocol.SerializeCommand(GuardCommand.Request);

        Assert.True(GuardProtocol.TryParseRequest(json, out GuardRequest request));
        Assert.Equal(GuardPurpose.Test, request.Purpose);
    }

    [Fact]
    public void Unknown_purpose_is_rejected()
    {
        const string json = """{"command":"request","purpose":"unknown-purpose"}""";

        Assert.False(GuardProtocol.TryParseRequest(json, out _));
    }

    [Fact]
    public void Response_round_trips_as_json()
    {
        string json = GuardResponse.FromStatus(GuardStatus.StoppedByUser).ToJson();

        GuardResponse response = GuardProtocol.ParseResponse(json);

        Assert.Equal(GuardStatus.StoppedByUser, response.Status);
        Assert.Equal(ExitCodes.StoppedByUser, response.ExitCode);
        Assert.Equal("Тестирование остановлено пользователем", response.Message);
    }

    [Fact]
    public void Timeout_response_round_trips_as_json()
    {
        string json = GuardResponse.FromStatus(GuardStatus.ClosedByTimeout).ToJson();

        GuardResponse response = GuardProtocol.ParseResponse(json);

        Assert.Equal(GuardStatus.ClosedByTimeout, response.Status);
        Assert.Equal(ExitCodes.ClosedByTimeout, response.ExitCode);
        Assert.Equal("Плашка закрыта по таймауту", response.Message);
    }

    [Fact]
    public void Response_round_trips_with_owner_and_start_time()
    {
        DateTimeOffset startedAtUtc = new(2026, 5, 20, 0, 32, 21, TimeSpan.Zero);
        string json = GuardResponse.FromStatus(
                GuardStatus.Running,
                owner: "chat-1",
                startedAtUtc: startedAtUtc)
            .ToJson();

        GuardResponse response = GuardProtocol.ParseResponse(json);

        Assert.Equal(GuardStatus.Running, response.Status);
        Assert.Equal("chat-1", response.Owner);
        Assert.Equal(startedAtUtc, response.StartedAtUtc);
    }

    [Fact]
    public void Unknown_response_status_returns_protocol_error()
    {
        GuardResponse response = GuardProtocol.ParseResponse("""{"status":"unknown_state"}""");

        Assert.Equal(GuardStatus.ProtocolError, response.Status);
        Assert.Equal(ExitCodes.ProtocolError, response.ExitCode);
    }
}
