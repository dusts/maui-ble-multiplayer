namespace MauiBle.Core;

/// <summary>
/// BLE scan-response local name. Legacy advertising leaves little room once a
/// 128-bit service UUID is in the primary packet, so the name stays short.
/// </summary>
public static class BleAdvertiseName
{
    /// <summary>
    /// Max Complete Local Name length that still fits a typical scan response.
    /// Longer names often fail with AdvertiseFailure.DataTooLarge.
    /// </summary>
    public const int MaxAdvertisedLength = 18;

    public static string FromDisplayName(string? displayName)
    {
        var core = Sanitize(displayName);
        if (string.IsNullOrEmpty(core))
            core = "Host";

        var withSpace = $"{BleIds.LocalNamePrefix} {core}";
        if (withSpace.Length <= MaxAdvertisedLength)
            return withSpace;

        var budget = MaxAdvertisedLength - (BleIds.LocalNamePrefix.Length + 1);
        if (budget < 1)
            return BleIds.LocalNamePrefix;

        var truncated = core.Length > budget ? core[..budget].TrimEnd() : core;
        return $"{BleIds.LocalNamePrefix} {truncated}";
    }

    /// <summary>
    /// Player-facing label. Strips the <see cref="BleIds.LocalNamePrefix"/> so
    /// "MBM Ada" shows as "Ada". Returns null when the advertisement has no usable name.
    /// </summary>
    public static string? ToUiLabel(string? advertisedName)
    {
        if (string.IsNullOrWhiteSpace(advertisedName)
            || advertisedName is "(no name)" or "N/A")
            return null;

        var n = advertisedName.Trim();
        if (n.StartsWith(BleIds.LocalNamePrefix, StringComparison.OrdinalIgnoreCase)
            && n.Length > BleIds.LocalNamePrefix.Length)
        {
            var rest = n[BleIds.LocalNamePrefix.Length..].TrimStart(' ', '-', '_');
            return string.IsNullOrWhiteSpace(rest) ? null : rest.Trim();
        }

        return n;
    }

    /// <summary>RSSI in dBm. Higher (closer to zero) is stronger. Zero means unknown.</summary>
    public static string FormatSignal(int rssiDbm)
    {
        if (rssiDbm == 0)
            return "signal ?";

        var band = rssiDbm switch
        {
            >= -55 => "Excellent",
            >= -65 => "Good",
            >= -75 => "Fair",
            >= -85 => "Weak",
            _ => "Very weak",
        };
        return $"{band} ({rssiDbm} dBm)";
    }

    public static string FormatHostButton(string? advertisedName, int rssiDbm)
    {
        var label = ToUiLabel(advertisedName) ?? "Nearby host";
        return $"{label}  ·  {FormatSignal(rssiDbm)}";
    }

    private static string Sanitize(string? displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            return "";

        var chars = displayName.Trim()
            .Select(c => char.IsLetterOrDigit(c) ? c : (char.IsWhiteSpace(c) ? ' ' : '\0'))
            .Where(c => c != '\0')
            .ToArray();
        var s = new string(chars);
        while (s.Contains("  ", StringComparison.Ordinal))
            s = s.Replace("  ", " ", StringComparison.Ordinal);
        return s.Trim();
    }
}
