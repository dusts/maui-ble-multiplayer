namespace MauiBle.Sample.Bluetooth;

public sealed class PeerEventArgs : EventArgs
{
    public required string TransportId { get; init; }
    public string? Name { get; init; }
}

public sealed class IncomingBleMessageEventArgs : EventArgs
{
    /// <summary>BLE address of the sender. Null on a notification received by a guest.</summary>
    public string? TransportId { get; init; }
    public required string Json { get; init; }
}

public sealed class NearbyHost
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public int SignalStrength { get; init; }
}

/// <summary>
/// BLE transport only. Lobby rules sit above this and exchange JSON strings.
/// Never construct Plugin.BLE in a DI constructor or at app startup.
/// </summary>
public interface IBleTransport
{
    bool IsConnected { get; }
    int ConnectedPeerCount { get; }
    bool IsHosting { get; }

    event EventHandler<IncomingBleMessageEventArgs>? MessageReceived;
    event EventHandler<string>? StatusChanged;
    event EventHandler<PeerEventArgs>? PeerJoined;
    event EventHandler<PeerEventArgs>? PeerLeft;
    event EventHandler<IReadOnlyList<NearbyHost>>? NearbyHostsUpdated;

    IReadOnlyList<NearbyHost> GetDiscoveredHosts();

    Task StartHostingAsync(string displayName, CancellationToken cancellationToken = default);
    Task StopHostingAsync();
    Task StartScanningAsync(CancellationToken cancellationToken = default);
    Task StopScanningAsync();
    Task ConnectAsync(NearbyHost host, CancellationToken cancellationToken = default);
    Task DisconnectAsync();

    /// <summary>Host: notify every guest. Guest: write the RX characteristic.</summary>
    Task SendAsync(string json, CancellationToken cancellationToken = default);
}
