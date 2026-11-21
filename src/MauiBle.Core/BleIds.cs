namespace MauiBle.Core;

/// <summary>
/// Sample service and characteristic UUIDs. Generate new ones before you ship
/// another app so two products do not discover each other.
/// </summary>
public static class BleIds
{
    public const string Service = "6f0e1a10-4c3a-4b7e-9d11-0a1b2c3d4e50";

    /// <summary>Guest to host (central writes).</summary>
    public const string RxCharacteristic = "6f0e1a10-4c3a-4b7e-9d11-0a1b2c3d4e51";

    /// <summary>Host to guests (peripheral notifies).</summary>
    public const string TxCharacteristic = "6f0e1a10-4c3a-4b7e-9d11-0a1b2c3d4e52";

    public static Guid ServiceGuid { get; } = Guid.Parse(Service);
    public static Guid RxGuid { get; } = Guid.Parse(RxCharacteristic);
    public static Guid TxGuid { get; } = Guid.Parse(TxCharacteristic);

    /// <summary>Short prefix for the scan-response local name, for example "MBM Ada".</summary>
    public const string LocalNamePrefix = "MBM";
}
