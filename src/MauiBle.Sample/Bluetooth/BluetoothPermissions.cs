namespace MauiBle.Sample.Bluetooth;

public static class BluetoothPermissions
{
    public static async Task<bool> EnsureAsync()
    {
#if ANDROID
        var status = await Permissions.CheckStatusAsync<BluetoothMauiPermissions>();
        if (status == PermissionStatus.Granted)
            return true;

        status = await Permissions.RequestAsync<BluetoothMauiPermissions>();
        return status == PermissionStatus.Granted;
#else
        await Task.CompletedTask;
        return true;
#endif
    }
}

#if ANDROID
/// <summary>Runtime Bluetooth permissions required on Android.</summary>
public sealed class BluetoothMauiPermissions : Permissions.BasePlatformPermission
{
    public override (string androidPermission, bool isRuntime)[] RequiredPermissions
    {
        get
        {
            var list = new List<(string, bool)>();

            if (OperatingSystem.IsAndroidVersionAtLeast(31))
            {
                list.Add((global::Android.Manifest.Permission.BluetoothScan, true));
                list.Add((global::Android.Manifest.Permission.BluetoothConnect, true));
                list.Add((global::Android.Manifest.Permission.BluetoothAdvertise, true));
            }
            else
            {
                list.Add((global::Android.Manifest.Permission.AccessFineLocation, true));
                list.Add((global::Android.Manifest.Permission.AccessCoarseLocation, true));
            }

            return list.ToArray();
        }
    }
}
#endif
