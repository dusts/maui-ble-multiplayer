namespace MauiBle.Core;

public sealed class LobbyPlayer
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? TransportId { get; init; }
}

/// <summary>
/// Host-owned seats. A GATT connection is not a seat. Only hello adds a guest,
/// so a phone that connects and never says hello cannot block the lobby.
/// </summary>
public sealed class LobbyRoster
{
    private readonly Dictionary<string, LobbyPlayer> _guests = new(StringComparer.OrdinalIgnoreCase);

    public LobbyPlayer? Host { get; private set; }

    public void SetHost(string id, string name)
    {
        Host = new LobbyPlayer
        {
            Id = id,
            Name = string.IsNullOrWhiteSpace(name) ? "Host" : name.Trim(),
        };
    }

    public bool TrySeatGuest(string transportId, string? playerId, string? name, out string? reject)
    {
        reject = null;
        if (string.IsNullOrWhiteSpace(transportId) || string.IsNullOrWhiteSpace(playerId))
        {
            reject = "Hello is missing a transport id or player id.";
            return false;
        }

        var known = _guests.ContainsKey(transportId);
        if (!known && _guests.Count >= BleLobbyLimits.MaxGuests)
        {
            reject = $"Lobby is full ({BleLobbyLimits.MaxPlayers} players).";
            return false;
        }

        _guests[transportId] = new LobbyPlayer
        {
            Id = playerId.Trim(),
            Name = string.IsNullOrWhiteSpace(name) ? "Guest" : name.Trim(),
            TransportId = transportId,
        };
        return true;
    }

    public bool RemoveGuest(string transportId) =>
        !string.IsNullOrEmpty(transportId) && _guests.Remove(transportId);

    public IReadOnlyList<LobbyPlayer> Players
    {
        get
        {
            var list = new List<LobbyPlayer>();
            if (Host is not null)
                list.Add(Host);
            list.AddRange(_guests.Values);
            return list;
        }
    }
}
