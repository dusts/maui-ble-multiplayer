#if ANDROID
using System.Text;
using Android.Bluetooth;
using Android.Bluetooth.LE;
using Android.Content;
using Android.OS;
using Android.Runtime;
using Java.Util;
using MauiBle.Core;
using Application = Android.App.Application;

namespace MauiBle.Sample.Platforms.Android.Bluetooth;

/// <summary>
/// GATT server and advertiser for the Android host.
/// Tracks every connected central and notifies all of them.
/// </summary>
public sealed class AndroidBlePeripheral : IDisposable
{
    private readonly object _gate = new();
    private BluetoothManager? _manager;
    private BluetoothGattServer? _gattServer;
    private BluetoothLeAdvertiser? _advertiser;
    private BluetoothGattCharacteristic? _txCharacteristic;
    private BluetoothGattCharacteristic? _rxCharacteristic;
    private readonly Dictionary<string, BluetoothDevice> _connected = new(StringComparer.OrdinalIgnoreCase);
    private AdvertiseCallback? _advertiseCallback;
    private GattServerCallback? _serverCallback;
    private string _localName = BleIds.LocalNamePrefix;
    private string? _previousAdapterName;
    private byte[] _lastTxValue = [];

    public event EventHandler<string>? PeerConnected;
    public event EventHandler<(string Id, string? Name)>? PeerConnectedDetailed;
    public event EventHandler<string>? PeerDisconnected;
    public event EventHandler<(string TransportId, string Json)>? MessageReceivedFrom;
    public event EventHandler<string>? StatusChanged;

    public int ConnectedCount
    {
        get { lock (_gate) return _connected.Count; }
    }

    public bool IsConnected => ConnectedCount > 0;

    public Task StartAsync(string displayName, CancellationToken cancellationToken = default)
    {
        StopInternal();

        var context = Application.Context;
        _manager = (BluetoothManager?)context.GetSystemService(Context.BluetoothService)
                   ?? throw new InvalidOperationException("BluetoothManager unavailable.");

        var adapter = _manager.Adapter
                      ?? throw new InvalidOperationException("Bluetooth adapter unavailable.");

        if (!adapter.IsEnabled)
            throw new InvalidOperationException("Bluetooth is turned off. Enable Bluetooth and try again.");

        _localName = BleAdvertiseName.FromDisplayName(displayName);
        try
        {
            _previousAdapterName = adapter.Name;
            adapter.SetName(_localName);
        }
        catch
        {
            // The service UUID is still advertised if the adapter rejects the name.
        }

        _serverCallback = new GattServerCallback(this);
        _gattServer = _manager.OpenGattServer(context, _serverCallback)
                      ?? throw new InvalidOperationException("Unable to open GATT server.");

        var service = new BluetoothGattService(
            UUID.FromString(BleIds.Service),
            GattServiceType.Primary);

        _rxCharacteristic = new BluetoothGattCharacteristic(
            UUID.FromString(BleIds.RxCharacteristic),
            GattProperty.Write | GattProperty.WriteNoResponse,
            GattPermission.Write);

        _txCharacteristic = new BluetoothGattCharacteristic(
            UUID.FromString(BleIds.TxCharacteristic),
            GattProperty.Read | GattProperty.Notify,
            GattPermission.Read);

        var cccd = new BluetoothGattDescriptor(
            UUID.FromString("00002902-0000-1000-8000-00805f9b34fb"),
            GattDescriptorPermission.Read | GattDescriptorPermission.Write);
        _txCharacteristic.AddDescriptor(cccd);

        service.AddCharacteristic(_rxCharacteristic);
        service.AddCharacteristic(_txCharacteristic);
        _gattServer.AddService(service);

        _advertiser = adapter.BluetoothLeAdvertiser
                      ?? throw new InvalidOperationException("BLE advertising is not supported on this device.");

        StartAdvertisingSafe(includeNameInScanResponse: true);
        RaiseStatus($"Hosting (up to {BleLobbyLimits.MaxPlayers} players). Waiting for joins.");
        return Task.CompletedTask;
    }

    private void StartAdvertisingSafe(bool includeNameInScanResponse)
    {
        if (_advertiser is null)
            return;

        try
        {
            if (_advertiseCallback is not null)
                _advertiser.StopAdvertising(_advertiseCallback);
        }
        catch
        {
            // A failed stop should not block the next advertise attempt.
        }

        var settings = new AdvertiseSettings.Builder()!
            .SetAdvertiseMode(AdvertiseMode.LowLatency)!
            .SetTxPowerLevel(AdvertiseTx.PowerMedium)!
            .SetConnectable(true)!
            .Build();

        // Primary advertisement is the service UUID only. A long name here is DataTooLarge.
        var data = new AdvertiseData.Builder()!
            .SetIncludeDeviceName(false)!
            .SetIncludeTxPowerLevel(false)!
            .AddServiceUuid(ParcelUuid.FromString(BleIds.Service))!
            .Build();

        AdvertiseData? scanResponse = null;
        if (includeNameInScanResponse)
        {
            scanResponse = new AdvertiseData.Builder()!
                .SetIncludeDeviceName(true)!
                .SetIncludeTxPowerLevel(false)!
                .Build();
        }

        _advertiseCallback = new AdvertiseCallbackImpl(this, allowNameFallback: includeNameInScanResponse);
        _advertiser.StartAdvertising(settings, data, scanResponse, _advertiseCallback);
    }

    public Task StopAsync()
    {
        StopInternal();
        return Task.CompletedTask;
    }

    /// <summary>Notify every connected guest. Large payloads should already be framed.</summary>
    public Task SendAsync(string json, CancellationToken cancellationToken = default)
    {
        List<BluetoothDevice> devices;
        BluetoothGattCharacteristic? tx;
        BluetoothGattServer? server;

        lock (_gate)
        {
            devices = _connected.Values.ToList();
            tx = _txCharacteristic;
            server = _gattServer;
        }

        if (tx is null || server is null || devices.Count == 0)
            return Task.CompletedTask;

        var bytes = Encoding.UTF8.GetBytes(json);
        if (bytes.Length > 500)
            bytes = bytes.AsSpan(0, 500).ToArray();

        lock (_gate)
            _lastTxValue = bytes;

        foreach (var device in devices)
        {
            try
            {
                NotifyTx(server, device, tx, bytes);
            }
            catch
            {
                // One central failing to notify must not stop the others.
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// API 33 takes the value on NotifyCharacteristicChanged.
    /// Older APIs still need SetValue plus the three-argument notify.
    /// </summary>
    private static void NotifyTx(
        BluetoothGattServer server,
        BluetoothDevice device,
        BluetoothGattCharacteristic tx,
        byte[] bytes)
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(33))
        {
            server.NotifyCharacteristicChanged(device, tx, confirm: false, bytes);
            return;
        }

#pragma warning disable CA1422
        tx.SetValue(bytes);
        server.NotifyCharacteristicChanged(device, tx, false);
#pragma warning restore CA1422
    }

    private byte[] ReadTxValue(BluetoothGattCharacteristic characteristic)
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(33))
        {
            lock (_gate)
                return _lastTxValue.Length == 0 ? [] : (byte[])_lastTxValue.Clone();
        }

#pragma warning disable CA1422
        var value = characteristic.GetValue();
#pragma warning restore CA1422
        if (value is { Length: > 0 })
            return value;

        lock (_gate)
            return _lastTxValue.Length == 0 ? [] : (byte[])_lastTxValue.Clone();
    }

    internal void OnDeviceConnected(BluetoothDevice device)
    {
        var id = device.Address ?? Guid.NewGuid().ToString("N");

        lock (_gate)
        {
            if (_connected.Count >= BleLobbyLimits.MaxGuests && !_connected.ContainsKey(id))
            {
                RaiseStatus($"Lobby full ({BleLobbyLimits.MaxPlayers} max). Rejecting an extra player.");
                try { _gattServer?.CancelConnection(device); } catch { /* ignore */ }
                return;
            }

            _connected[id] = device;
        }

        RaiseStatus($"Player connected ({ConnectedCount} guest(s)): {device.Name ?? id}");
        PeerConnected?.Invoke(this, id);
        PeerConnectedDetailed?.Invoke(this, (id, device.Name));
    }

    internal void OnDeviceDisconnected(BluetoothDevice device)
    {
        var id = device.Address ?? string.Empty;
        var removed = false;
        lock (_gate)
        {
            if (_connected.Remove(id))
                removed = true;
            else
            {
                var key = _connected.FirstOrDefault(kv => kv.Value.Address == device.Address).Key;
                if (key is not null && _connected.Remove(key))
                {
                    id = key;
                    removed = true;
                }
            }
        }

        if (removed)
        {
            RaiseStatus($"Player disconnected ({ConnectedCount} guest(s) left).");
            PeerDisconnected?.Invoke(this, id);
        }
    }

    internal void OnWrite(BluetoothDevice device, byte[] value)
    {
        var id = device.Address ?? string.Empty;
        var json = Encoding.UTF8.GetString(value);
        if (!string.IsNullOrWhiteSpace(json))
            MessageReceivedFrom?.Invoke(this, (id, json));
    }

    internal void OnAdvertiseFailure(string reason) =>
        RaiseStatus($"Advertising failed: {reason}");

    private void StopInternal()
    {
        try
        {
            if (_advertiser is not null && _advertiseCallback is not null)
                _advertiser.StopAdvertising(_advertiseCallback);
        }
        catch
        {
            // Stopping an advertiser that already stopped is fine.
        }

        try { _gattServer?.Close(); } catch { /* ignore */ }

        if (!string.IsNullOrEmpty(_previousAdapterName))
        {
            try
            {
                _manager?.Adapter?.SetName(_previousAdapterName);
            }
            catch
            {
                // Restoring the phone name is best-effort.
            }

            _previousAdapterName = null;
        }

        lock (_gate)
        {
            _connected.Clear();
            _gattServer = null;
            _advertiser = null;
            _txCharacteristic = null;
            _rxCharacteristic = null;
            _advertiseCallback = null;
            _serverCallback = null;
        }
    }

    private void RaiseStatus(string status) => StatusChanged?.Invoke(this, status);

    public void Dispose() => StopInternal();

    private sealed class AdvertiseCallbackImpl : AdvertiseCallback
    {
        private readonly AndroidBlePeripheral _owner;
        private readonly bool _allowNameFallback;

        public AdvertiseCallbackImpl(AndroidBlePeripheral owner, bool allowNameFallback)
        {
            _owner = owner;
            _allowNameFallback = allowNameFallback;
        }

        public override void OnStartSuccess(AdvertiseSettings? settingsInEffect) =>
            _owner.RaiseStatus($"Hosting (up to {BleLobbyLimits.MaxPlayers}). Waiting for joins.");

        public override void OnStartFailure([GeneratedEnum] AdvertiseFailure errorCode)
        {
            if (errorCode == AdvertiseFailure.DataTooLarge && _allowNameFallback)
            {
                _owner.RaiseStatus("Advertise data too large. Retrying without the name.");
                try
                {
                    _owner.StartAdvertisingSafe(includeNameInScanResponse: false);
                    return;
                }
                catch (Exception ex)
                {
                    _owner.OnAdvertiseFailure($"{errorCode} (retry failed: {ex.Message})");
                    return;
                }
            }

            _owner.OnAdvertiseFailure(errorCode.ToString());
        }
    }

    private sealed class GattServerCallback : BluetoothGattServerCallback
    {
        private readonly AndroidBlePeripheral _owner;

        public GattServerCallback(AndroidBlePeripheral owner) => _owner = owner;

        public override void OnConnectionStateChange(
            BluetoothDevice? device,
            [GeneratedEnum] ProfileState status,
            [GeneratedEnum] ProfileState newState)
        {
            if (device is null)
                return;

            if (newState == ProfileState.Connected)
                _owner.OnDeviceConnected(device);
            else if (newState == ProfileState.Disconnected)
                _owner.OnDeviceDisconnected(device);
        }

        public override void OnCharacteristicWriteRequest(
            BluetoothDevice? device,
            int requestId,
            BluetoothGattCharacteristic? characteristic,
            bool preparedWrite,
            bool responseNeeded,
            int offset,
            byte[]? value)
        {
            if (device is null || characteristic is null)
                return;

            if (characteristic.Uuid?.ToString().Equals(BleIds.RxCharacteristic, StringComparison.OrdinalIgnoreCase) == true
                && value is { Length: > 0 })
            {
                _owner.OnWrite(device, value);
            }

            if (responseNeeded)
                _owner._gattServer?.SendResponse(device, requestId, GattStatus.Success, offset, value ?? []);
        }

        public override void OnDescriptorWriteRequest(
            BluetoothDevice? device,
            int requestId,
            BluetoothGattDescriptor? descriptor,
            bool preparedWrite,
            bool responseNeeded,
            int offset,
            byte[]? value)
        {
            if (device is null)
                return;

            if (responseNeeded)
                _owner._gattServer?.SendResponse(device, requestId, GattStatus.Success, offset, value ?? []);
        }

        public override void OnCharacteristicReadRequest(
            BluetoothDevice? device,
            int requestId,
            int offset,
            BluetoothGattCharacteristic? characteristic)
        {
            if (device is null || characteristic is null)
                return;

            var value = _owner.ReadTxValue(characteristic);
            _owner._gattServer?.SendResponse(device, requestId, GattStatus.Success, offset, value);
        }
    }
}
#endif
