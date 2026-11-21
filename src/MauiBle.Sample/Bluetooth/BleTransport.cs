using System.Collections.Concurrent;
using System.Text;
using MauiBle.Core;
using Microsoft.Extensions.Logging;
using Plugin.BLE;
using Plugin.BLE.Abstractions;
using Plugin.BLE.Abstractions.Contracts;
using Plugin.BLE.Abstractions.EventArgs;
using Plugin.BLE.Abstractions.Exceptions;
#if ANDROID
using Android.Bluetooth;
using Android.Content;
using MauiBle.Sample.Platforms.Android.Bluetooth;
using Application = Android.App.Application;
#endif

namespace MauiBle.Sample.Bluetooth;

/// <summary>
/// Plugin.BLE guest and, on Android, a native GATT host.
/// Plugin.BLE is created on the first scan or connect. Constructing it at
/// startup can hard-crash some Samsung phones.
/// </summary>
public sealed class BleTransport : IBleTransport, IDisposable
{
    private readonly ConcurrentDictionary<string, IDevice> _discovered = new();
    private readonly ConcurrentDictionary<string, FrameReassembly> _reassembly = new(StringComparer.OrdinalIgnoreCase);
    private readonly FrameReassembly _guestReassembly = new();
    private readonly object _gate = new();
    private readonly ILogger<BleTransport>? _log;

    private IBluetoothLE? _ble;
    private IAdapter? _adapter;
    private bool _bleHooksAttached;
    private IDevice? _connectedDevice;
    private ICharacteristic? _rxCharacteristic;
    private ICharacteristic? _txCharacteristic;
    private bool _isHost;
    private bool _disposed;

#if ANDROID
    private readonly AndroidBlePeripheral _peripheral = new();
#endif

    public BleTransport(ILogger<BleTransport>? log = null)
    {
        _log = log;
        _log?.LogInformation("BleTransport constructed (Bluetooth stack not touched yet)");

#if ANDROID
        _peripheral.PeerConnectedDetailed += (_, e) =>
        {
            IsConnected = _peripheral.IsConnected;
            _log?.LogInformation("Host peer joined: {Name} (guests={Count})", e.Name ?? e.Id, _peripheral.ConnectedCount);
            PeerJoined?.Invoke(this, new PeerEventArgs { TransportId = e.Id, Name = e.Name });
        };
        _peripheral.PeerDisconnected += (_, transportId) =>
        {
            IsConnected = _peripheral.IsConnected;
            _reassembly.TryRemove(transportId, out FrameReassembly? _);
            _log?.LogWarning("Host peer left: {Id} (guests={Count})", transportId, _peripheral.ConnectedCount);
            PeerLeft?.Invoke(this, new PeerEventArgs { TransportId = transportId });
        };
        _peripheral.MessageReceivedFrom += (_, e) => HandleIncoming(e.TransportId, e.Json);
        _peripheral.StatusChanged += (_, status) =>
        {
            _log?.LogInformation("Host status: {Status}", status);
            StatusChanged?.Invoke(this, status);
        };
#endif
    }

    public bool IsConnected { get; private set; }
    public bool IsHosting => _isHost;

#if ANDROID
    public int ConnectedPeerCount => _isHost ? _peripheral.ConnectedCount : (IsConnected ? 1 : 0);
#else
    public int ConnectedPeerCount => IsConnected ? 1 : 0;
#endif

    public event EventHandler<IncomingBleMessageEventArgs>? MessageReceived;
    public event EventHandler<string>? StatusChanged;
    public event EventHandler<PeerEventArgs>? PeerJoined;
    public event EventHandler<PeerEventArgs>? PeerLeft;
    public event EventHandler<IReadOnlyList<NearbyHost>>? NearbyHostsUpdated;

    public IReadOnlyList<NearbyHost> GetDiscoveredHosts() => BuildHostList();

    public async Task StartHostingAsync(string displayName, CancellationToken cancellationToken = default)
    {
        await StopScanningAsync();
        await DisconnectClientAsync();

        if (!await BluetoothPermissions.EnsureAsync())
            throw new InvalidOperationException("Bluetooth permission was denied.");

        EnsureSystemBluetoothOn();
        _isHost = true;
        _log?.LogInformation("StartHosting name={Name}", displayName);

#if ANDROID
        try
        {
            await _peripheral.StartAsync(displayName, cancellationToken);
        }
        catch (Exception ex)
        {
            _log?.LogError(ex, "StartHosting failed");
            throw;
        }
#else
        throw new PlatformNotSupportedException(
            "Hosting is Android-only in this sample. Join from this device, or host from an Android phone.");
#endif
    }

    public async Task StopHostingAsync()
    {
#if ANDROID
        await _peripheral.StopAsync();
#endif
        if (_isHost)
        {
            IsConnected = false;
            _isHost = false;
        }
    }

    public async Task StartScanningAsync(CancellationToken cancellationToken = default)
    {
        await StopHostingAsync();
        await DisconnectClientAsync();

        if (!await BluetoothPermissions.EnsureAsync())
            throw new InvalidOperationException("Bluetooth permission was denied.");

        var adapter = EnsurePluginBle();
        EnsureAdapterOn();

        _isHost = false;
        _discovered.Clear();
        NearbyHostsUpdated?.Invoke(this, Array.Empty<NearbyHost>());
        RaiseStatus("Scanning for hosts…");

        if (adapter.IsScanning)
            await adapter.StopScanningForDevicesAsync();

        adapter.ScanTimeout = 45_000;
        try
        {
            await adapter.StartScanningForDevicesAsync(
                new[] { BleIds.ServiceGuid },
                deviceFilter: null,
                allowDuplicatesKey: true,
                cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _log?.LogWarning("Filtered scan failed: {Message}", ex.Message);
            RaiseStatus($"Filtered scan failed ({ex.Message}). Scanning all devices.");
            await adapter.StartScanningForDevicesAsync(
                deviceFilter: null,
                allowDuplicatesKey: true,
                cancellationToken: cancellationToken);
        }

        PublishHosts();
    }

    public async Task StopScanningAsync()
    {
        try
        {
            if (_adapter?.IsScanning == true)
                await _adapter.StopScanningForDevicesAsync();
        }
        catch
        {
            // The scan may already have stopped.
        }
    }

    public async Task ConnectAsync(NearbyHost host, CancellationToken cancellationToken = default)
    {
        await StopScanningAsync();

        if (!await BluetoothPermissions.EnsureAsync())
            throw new InvalidOperationException("Bluetooth permission was denied.");

        var adapter = EnsurePluginBle();
        EnsureAdapterOn();

        _isHost = false;
        RaiseStatus($"Connecting to {host.DisplayName}…");

        if (!_discovered.TryGetValue(host.Id, out var device))
            throw new InvalidOperationException("That host is no longer in the scan. Scan again.");

        try
        {
            await adapter.ConnectToDeviceAsync(
                device,
                new ConnectParameters(autoConnect: false, forceBleTransport: true),
                cancellationToken);
            _connectedDevice = device;

            try
            {
                await device.RequestMtuAsync(512);
            }
            catch
            {
                // A rejected MTU request still leaves the default link up.
            }

            var services = await device.GetServicesAsync(cancellationToken);
            var service = services.FirstOrDefault(s => s.Id == BleIds.ServiceGuid)
                          ?? throw new InvalidOperationException("The device does not expose this sample's service.");

            var characteristics = await service.GetCharacteristicsAsync();
            _rxCharacteristic = characteristics.FirstOrDefault(c => c.Id == BleIds.RxGuid)
                                ?? throw new InvalidOperationException("RX characteristic is missing.");
            _txCharacteristic = characteristics.FirstOrDefault(c => c.Id == BleIds.TxGuid)
                                ?? throw new InvalidOperationException("TX characteristic is missing.");

            _txCharacteristic.ValueUpdated -= OnTxValueUpdated;
            _txCharacteristic.ValueUpdated += OnTxValueUpdated;
            await _txCharacteristic.StartUpdatesAsync();

            IsConnected = true;
            RaiseStatus($"Connected to {host.DisplayName}");
            PeerJoined?.Invoke(this, new PeerEventArgs { TransportId = host.Id, Name = host.DisplayName });
        }
        catch (DeviceConnectionException ex)
        {
            IsConnected = false;
            _log?.LogError(ex, "ConnectAsync failed");
            RaiseStatus($"Connection failed: {ex.Message}");
            throw;
        }
        catch (Exception ex)
        {
            IsConnected = false;
            _log?.LogError(ex, "ConnectAsync failed");
            throw;
        }
    }

    public async Task DisconnectAsync()
    {
        await StopScanningAsync();
        await StopHostingAsync();
        await DisconnectClientAsync();
        IsConnected = false;
        _reassembly.Clear();
        _guestReassembly.Reset();
    }

    public async Task SendAsync(string json, CancellationToken cancellationToken = default)
    {
        var frames = BleFraming.Encode(json);

        if (_isHost)
        {
#if ANDROID
            foreach (var frame in frames)
                await _peripheral.SendAsync(frame, cancellationToken);
            if (_peripheral.IsConnected)
                IsConnected = true;
#else
            await Task.CompletedTask;
#endif
            return;
        }

        ICharacteristic? rx;
        lock (_gate)
            rx = _rxCharacteristic;

        if (rx is null || !IsConnected)
            return;

        foreach (var frame in frames)
            await rx.WriteAsync(Encoding.UTF8.GetBytes(frame), cancellationToken);
    }

    private void HandleIncoming(string? transportId, string raw)
    {
        try
        {
            var buffer = string.IsNullOrEmpty(transportId)
                ? _guestReassembly
                : _reassembly.GetOrAdd(transportId, _ => new FrameReassembly());

            var complete = BleFraming.Feed(raw, buffer);
            if (complete is null)
                return;

            MessageReceived?.Invoke(this, new IncomingBleMessageEventArgs
            {
                TransportId = transportId,
                Json = complete,
            });
        }
        catch (Exception ex)
        {
            _log?.LogError(ex, "HandleIncoming failed");
        }
    }

    private IAdapter EnsurePluginBle()
    {
        lock (_gate)
        {
            if (_adapter is not null && _ble is not null)
                return _adapter;

            try
            {
                _ble = CrossBluetoothLE.Current
                       ?? throw new InvalidOperationException("CrossBluetoothLE.Current is null.");
                _adapter = _ble.Adapter
                           ?? throw new InvalidOperationException("BLE adapter is null.");

                if (!_bleHooksAttached)
                {
                    _adapter.DeviceDiscovered += OnDeviceDiscovered;
                    _adapter.DeviceConnectionLost += OnDeviceConnectionLost;
                    _adapter.ScanTimeoutElapsed += OnScanTimeoutElapsed;
                    _bleHooksAttached = true;
                }

                return _adapter;
            }
            catch (Exception ex)
            {
                _log?.LogError(ex, "Plugin.BLE initialization failed");
                throw new InvalidOperationException(
                    "Bluetooth LE failed to start. Toggle Bluetooth or reboot the phone.", ex);
            }
        }
    }

    private void EnsureSystemBluetoothOn()
    {
#if ANDROID
        try
        {
            var manager = (BluetoothManager?)Application.Context.GetSystemService(Context.BluetoothService);
            var adapter = manager?.Adapter;
            if (adapter is null)
                throw new InvalidOperationException("Bluetooth adapter unavailable.");
            if (!adapter.IsEnabled)
                throw new InvalidOperationException("Bluetooth is turned off. Enable Bluetooth and try again.");
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            throw new InvalidOperationException("Unable to access Bluetooth on this device.", ex);
        }
#else
        EnsurePluginBle();
        EnsureAdapterOn();
#endif
    }

    private void OnDeviceDiscovered(object? sender, DeviceEventArgs e)
    {
        try
        {
            var device = e.Device;
            if (device is null)
                return;

            var id = device.Id.ToString();
            _discovered[id] = device;
            _log?.LogInformation("Discovered name='{Name}' id={Id} rssi={Rssi}", device.Name ?? "(no name)", id, device.Rssi);
            PublishHosts();
        }
        catch (Exception ex)
        {
            _log?.LogError(ex, "OnDeviceDiscovered failed");
        }
    }

    private void OnDeviceConnectionLost(object? sender, DeviceErrorEventArgs e)
    {
        if (_isHost)
            return;

        IsConnected = false;
        _log?.LogWarning("Connection lost: {Error}", e.ErrorMessage);
        RaiseStatus("Connection lost");
        PeerLeft?.Invoke(this, new PeerEventArgs { TransportId = _connectedDevice?.Id.ToString() ?? "" });
    }

    private void OnScanTimeoutElapsed(object? sender, EventArgs e)
    {
        PublishHosts();
        var msg = _discovered.IsEmpty
            ? "Scan finished. No hosts found."
            : $"Scan finished ({_discovered.Count} device(s)). Pick a host or scan again.";
        RaiseStatus(msg);
    }

    private List<NearbyHost> BuildHostList()
    {
        return _discovered.Select(kv =>
        {
            var advertised = kv.Value.Name ?? string.Empty;
            var label = BleAdvertiseName.ToUiLabel(advertised)
                        ?? $"Host ({kv.Key[..Math.Min(6, kv.Key.Length)]})";
            return new NearbyHost
            {
                Id = kv.Key,
                DisplayName = label,
                SignalStrength = kv.Value.Rssi,
            };
        }).OrderByDescending(h => h.SignalStrength).ToList();
    }

    private void PublishHosts()
    {
        var hosts = BuildHostList();
        NearbyHostsUpdated?.Invoke(this, hosts);
    }

    private void OnTxValueUpdated(object? sender, CharacteristicUpdatedEventArgs e)
    {
        try
        {
            var bytes = e.Characteristic.Value;
            if (bytes is null || bytes.Length == 0)
                return;

            HandleIncoming(null, Encoding.UTF8.GetString(bytes));
        }
        catch (Exception ex)
        {
            _log?.LogError(ex, "OnTxValueUpdated failed");
        }
    }

    private async Task DisconnectClientAsync()
    {
        try
        {
            if (_txCharacteristic is not null)
            {
                _txCharacteristic.ValueUpdated -= OnTxValueUpdated;
                try { await _txCharacteristic.StopUpdatesAsync(); } catch { /* ignore */ }
            }
        }
        catch
        {
            // Unsubscribing from a dead characteristic is best-effort.
        }

        _txCharacteristic = null;
        _rxCharacteristic = null;

        if (_connectedDevice is not null && _adapter is not null)
        {
            try
            {
                await _adapter.DisconnectDeviceAsync(_connectedDevice);
            }
            catch
            {
                // The link may already be gone.
            }

            _connectedDevice = null;
        }
    }

    private void EnsureAdapterOn()
    {
        var ble = _ble ?? throw new InvalidOperationException("BLE is not initialized.");
        if (!ble.IsAvailable)
            throw new InvalidOperationException("Bluetooth LE is not available on this device.");
        if (ble.State != BluetoothState.On)
            throw new InvalidOperationException("Bluetooth is turned off. Enable Bluetooth and try again.");
    }

    private void RaiseStatus(string status) => StatusChanged?.Invoke(this, status);

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        if (_adapter is not null && _bleHooksAttached)
        {
            _adapter.DeviceDiscovered -= OnDeviceDiscovered;
            _adapter.DeviceConnectionLost -= OnDeviceConnectionLost;
            _adapter.ScanTimeoutElapsed -= OnScanTimeoutElapsed;
        }

        try
        {
            DisconnectAsync().GetAwaiter().GetResult();
        }
        catch
        {
            // Dispose must not throw.
        }

#if ANDROID
        _peripheral.Dispose();
#endif
    }
}
