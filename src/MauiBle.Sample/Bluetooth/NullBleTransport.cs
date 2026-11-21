namespace MauiBle.Sample.Bluetooth;

/// <summary>
/// Used where this build cannot host or scan. Android registers <see cref="BleTransport"/> instead.
/// </summary>
public sealed class NullBleTransport : IBleTransport
{
    public bool IsConnected => false;
    public int ConnectedPeerCount => 0;
    public bool IsHosting => false;

#pragma warning disable CS0067
    public event EventHandler<IncomingBleMessageEventArgs>? MessageReceived;
    public event EventHandler<string>? StatusChanged;
    public event EventHandler<PeerEventArgs>? PeerJoined;
    public event EventHandler<PeerEventArgs>? PeerLeft;
    public event EventHandler<IReadOnlyList<NearbyHost>>? NearbyHostsUpdated;
#pragma warning restore CS0067

    public IReadOnlyList<NearbyHost> GetDiscoveredHosts() => Array.Empty<NearbyHost>();

    public Task StartHostingAsync(string displayName, CancellationToken cancellationToken = default)
    {
        StatusChanged?.Invoke(this, "Hosting is Android-only in this sample.");
        return Task.FromException(new PlatformNotSupportedException(
            "The GATT peripheral host is Android-only. Host from an Android phone."));
    }

    public Task StopHostingAsync() => Task.CompletedTask;

    public Task StartScanningAsync(CancellationToken cancellationToken = default)
    {
        StatusChanged?.Invoke(this, "Scanning needs Plugin.BLE on a device build.");
        NearbyHostsUpdated?.Invoke(this, Array.Empty<NearbyHost>());
        return Task.CompletedTask;
    }

    public Task StopScanningAsync() => Task.CompletedTask;

    public Task ConnectAsync(NearbyHost host, CancellationToken cancellationToken = default) =>
        Task.FromException(new PlatformNotSupportedException("BLE connect is not available in this build."));

    public Task DisconnectAsync() => Task.CompletedTask;

    public Task SendAsync(string json, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
