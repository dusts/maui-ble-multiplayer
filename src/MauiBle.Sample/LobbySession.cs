using MauiBle.Core;
using MauiBle.Sample.Bluetooth;

namespace MauiBle.Sample;

/// <summary>
/// Host-authoritative lobby over JSON strings. A connection is not a seat until hello arrives.
/// </summary>
public sealed class LobbySession : IDisposable
{
    private readonly IBleTransport _ble;
    private readonly LobbyRoster _roster = new();
    private readonly List<string> _lines = new();

    public LobbySession(IBleTransport ble)
    {
        _ble = ble;
        _ble.MessageReceived += OnMessage;
        _ble.PeerLeft += OnPeerLeft;
    }

    public string LocalPlayerId { get; } = Guid.NewGuid().ToString("N")[..8];
    public string LocalName { get; set; } = "Player";
    public string RosterLine { get; private set; } = "No one seated yet.";
    public string LogText { get; private set; } = "";
    public event EventHandler? Changed;

    public void SetHostIdentity()
    {
        _roster.SetHost(LocalPlayerId, LocalName);
        RosterLine = LobbyWire.FormatRoster(_roster.Players);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public Task SendHelloAsync() =>
        _ble.SendAsync(LobbyWire.Hello(LocalPlayerId, LocalName));

    public async Task SendChatAsync(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0)
            return;

        if (_ble.IsHosting)
        {
            AddLine($"{LocalName}: {trimmed}");
            await _ble.SendAsync(LobbyWire.Chat(LocalPlayerId, LocalName, trimmed));
            return;
        }

        await _ble.SendAsync(LobbyWire.Chat(LocalPlayerId, LocalName, trimmed));
    }

    public async Task SendDisconnectAsync()
    {
        try
        {
            await _ble.SendAsync(LobbyWire.Disconnect(LocalPlayerId, LocalName));
        }
        catch
        {
            // The link may already be down.
        }
    }

    public void Dispose()
    {
        _ble.MessageReceived -= OnMessage;
        _ble.PeerLeft -= OnPeerLeft;
    }

    private async void OnMessage(object? sender, IncomingBleMessageEventArgs e)
    {
        try
        {
            var msg = LobbyWire.Parse(e.Json);
            if (msg is null)
            {
                AddLine($"Ignored a message ({e.Json.Length} chars).");
                return;
            }

            if (_ble.IsHosting)
                await HandleAsHostAsync(e.TransportId, msg);
            else
                HandleAsGuest(msg);
        }
        catch (Exception ex)
        {
            AddLine(ex.Message);
        }
    }

    private async Task HandleAsHostAsync(string? transportId, LobbyMessage msg)
    {
        switch (msg.Type)
        {
            case "hello":
                if (string.IsNullOrEmpty(transportId))
                    return;
                if (!_roster.TrySeatGuest(transportId, msg.PlayerId, msg.Name, out var reason))
                {
                    AddLine(reason ?? "Hello rejected.");
                    return;
                }

                AddLine($"Seated {msg.Name}.");
                await _ble.SendAsync(LobbyWire.Roster(_roster.Players));
                RosterLine = LobbyWire.FormatRoster(_roster.Players);
                Changed?.Invoke(this, EventArgs.Empty);
                break;
            case "chat":
                AddLine($"{msg.Name}: {msg.Text}");
                await _ble.SendAsync(LobbyWire.Chat(msg.PlayerId, msg.Name, msg.Text));
                break;
            case "disconnect":
                if (string.IsNullOrEmpty(transportId) || !_roster.RemoveGuest(transportId))
                    return;
                AddLine($"{msg.Name ?? "Guest"} left.");
                await _ble.SendAsync(LobbyWire.Roster(_roster.Players));
                RosterLine = LobbyWire.FormatRoster(_roster.Players);
                Changed?.Invoke(this, EventArgs.Empty);
                break;
        }
    }

    private void HandleAsGuest(LobbyMessage msg)
    {
        switch (msg.Type)
        {
            case "roster":
                RosterLine = LobbyWire.FormatRoster(msg.Players);
                Changed?.Invoke(this, EventArgs.Empty);
                break;
            case "chat":
                AddLine($"{msg.Name}: {msg.Text}");
                break;
        }
    }

    private async void OnPeerLeft(object? sender, PeerEventArgs e)
    {
        try
        {
            if (!_ble.IsHosting)
            {
                AddLine("Disconnected from the host.");
                return;
            }

            if (!_roster.RemoveGuest(e.TransportId))
                return;

            AddLine("A guest disconnected.");
            await _ble.SendAsync(LobbyWire.Roster(_roster.Players));
            RosterLine = LobbyWire.FormatRoster(_roster.Players);
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            AddLine(ex.Message);
        }
    }

    private void AddLine(string line)
    {
        _lines.Add(line);
        if (_lines.Count > 12)
            _lines.RemoveAt(0);
        LogText = string.Join(Environment.NewLine, _lines);
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
