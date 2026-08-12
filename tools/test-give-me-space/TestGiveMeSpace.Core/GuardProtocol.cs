using System.Text.Json;
using System.Text.Json.Serialization;

namespace TestGiveMeSpace.Core;

public static class GuardProtocol
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string SerializeCommand(GuardCommand command)
        => SerializeCommand(command, GuardPurpose.Test);

    public static string SerializeCommand(GuardCommand command, GuardPurpose purpose)
        => SerializeCommand(command, purpose, owner: null);

    public static string SerializeCommand(
        GuardCommand command,
        GuardPurpose purpose,
        string? owner)
        => JsonSerializer.Serialize(
            new CommandShape(command.ToWireValue(), purpose.ToWireValue(), NormalizeOwner(owner), X: null, Y: null),
            JsonOptions);

    public static string SerializeRequest(GuardRequest request)
        => JsonSerializer.Serialize(
            new CommandShape(
                request.Command.ToWireValue(),
                request.Purpose.ToWireValue(),
                NormalizeOwner(request.Owner),
                request.X,
                request.Y),
            JsonOptions);

    public static bool TryParseCommand(string? json, out GuardCommand command)
    {
        command = default;
        if (!TryParseRequest(json, out GuardRequest request))
        {
            return false;
        }

        command = request.Command;
        return true;
    }

    public static bool TryParseRequest(string? json, out GuardRequest request)
    {
        request = default!;
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            CommandShape? shape = JsonSerializer.Deserialize<CommandShape>(json, JsonOptions);
            if (!GuardCommandExtensions.TryParseWireValue(shape?.Command, out GuardCommand command))
            {
                return false;
            }

            GuardPurpose purpose = GuardPurpose.Test;
            if (shape?.Purpose is not null
                && !GuardPurposeExtensions.TryParseWireValue(shape.Purpose, out purpose))
            {
                return false;
            }

            int? x = null;
            int? y = null;
            if (command is GuardCommand.AvoidPoint)
            {
                x = shape?.X;
                y = shape?.Y;
                if (!x.HasValue || !y.HasValue)
                {
                    return false;
                }
            }
            else if (shape?.X.HasValue is true || shape?.Y.HasValue is true)
            {
                return false;
            }

            request = new GuardRequest(command, purpose, NormalizeOwner(shape?.Owner), x, y);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static GuardResponse ParseResponse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return GuardResponse.FromStatus(GuardStatus.ProtocolError);
        }

        try
        {
            ResponseShape? shape = JsonSerializer.Deserialize<ResponseShape>(json, JsonOptions);
            if (!GuardStatusExtensions.TryParseWireValue(shape?.Status, out GuardStatus status))
            {
                return GuardResponse.FromStatus(GuardStatus.ProtocolError);
            }

            return GuardResponse.FromStatus(
                status,
                shape?.Message,
                NormalizeOwner(shape?.Owner),
                shape?.StartedAtUtc);
        }
        catch (JsonException)
        {
            return GuardResponse.FromStatus(GuardStatus.ProtocolError);
        }
    }

    private static string? NormalizeOwner(string? owner)
        => string.IsNullOrWhiteSpace(owner) ? null : owner.Trim();

    private sealed record CommandShape(
        [property: JsonPropertyName("command")] string? Command,
        [property: JsonPropertyName("purpose")] string? Purpose,
        [property: JsonPropertyName("owner")] string? Owner,
        [property: JsonPropertyName("x")] int? X,
        [property: JsonPropertyName("y")] int? Y);

    private sealed record ResponseShape(
        [property: JsonPropertyName("status")] string? Status,
        [property: JsonPropertyName("message")] string? Message,
        [property: JsonPropertyName("owner")] string? Owner,
        [property: JsonPropertyName("startedAtUtc")] DateTimeOffset? StartedAtUtc);
}
