using System.Text.Json;
using System.Text.Json.Serialization;

namespace MauiBle.Core;

public sealed class LobbyMessage
{
    public int V { get; set; }
    public string? Type { get; set; }
    public string? PlayerId { get; set; }
    public string? Name { get; set; }
    public string? Text { get; set; }
    public List<LobbyPlayer>? Players { get; set; }
}

/// <summary>
/// Small JSON messages for the sample lobby. The transport carries the string.
/// It does not know these types.
/// </summary>
public static class LobbyWire
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string Hello(string playerId, string name) =>
        Serialize(new LobbyMessage { V = 1, Type = "hello", PlayerId = playerId, Name = name });

    public static string Chat(string? playerId, string? name, string? text) =>
        Serialize(new LobbyMessage { V = 1, Type = "chat", PlayerId = playerId, Name = name, Text = text });

    public static string Disconnect(string playerId, string name) =>
        Serialize(new LobbyMessage { V = 1, Type = "disconnect", PlayerId = playerId, Name = name });

    public static string Roster(IReadOnlyList<LobbyPlayer> players)
    {
        var payload = players
            .Select(p => new LobbyPlayer { Id = p.Id, Name = p.Name })
            .ToList();
        return Serialize(new LobbyMessage { V = 1, Type = "roster", Players = payload });
    }

    public static LobbyMessage? Parse(string json)
    {
        try
        {
            var msg = JsonSerializer.Deserialize<LobbyMessage>(json, Options);
            if (msg is null || msg.V != 1 || string.IsNullOrEmpty(msg.Type))
                return null;
            return msg;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static string FormatRoster(IReadOnlyList<LobbyPlayer>? players)
    {
        if (players is null || players.Count == 0)
            return "No one seated yet.";
        return string.Join(", ", players.Select(p => p.Name));
    }

    private static string Serialize(LobbyMessage message) =>
        JsonSerializer.Serialize(message, Options);
}
