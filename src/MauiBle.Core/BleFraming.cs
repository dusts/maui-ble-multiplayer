using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MauiBle.Core;

/// <summary>
/// Splits a UTF-8 payload across BLE writes and notifications.
/// A part on the wire is <c>{"v":1,"type":"part","i":0,"n":3,"b":"..."}</c>.
/// Payloads at or under <see cref="MaxChunkPayloadBytes"/> are sent unchanged.
/// Application messages must not use type "part".
/// </summary>
public static class BleFraming
{
    /// <summary>
    /// Application bytes per part. The JSON wrapper is larger than this.
    /// Callers should request a larger MTU before sending.
    /// </summary>
    public const int MaxChunkPayloadBytes = 160;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static IReadOnlyList<string> Encode(string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        if (bytes.Length <= MaxChunkPayloadBytes)
            return [json];

        var parts = new List<string>();
        var n = (bytes.Length + MaxChunkPayloadBytes - 1) / MaxChunkPayloadBytes;
        for (var i = 0; i < n; i++)
        {
            var offset = i * MaxChunkPayloadBytes;
            var len = Math.Min(MaxChunkPayloadBytes, bytes.Length - offset);
            var slice = Convert.ToBase64String(bytes, offset, len);
            parts.Add(JsonSerializer.Serialize(new FramePart
            {
                V = 1,
                Type = "part",
                I = i,
                N = n,
                B = slice,
            }, Options));
        }

        return parts;
    }

    /// <summary>
    /// Feeds one raw BLE string. Returns the completed payload, or null while parts are still missing.
    /// </summary>
    public static string? Feed(string raw, FrameReassembly buffer)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        if (!LooksLikePart(raw))
        {
            buffer.Reset();
            return raw;
        }

        FramePart? part;
        try
        {
            part = JsonSerializer.Deserialize<FramePart>(raw, Options);
        }
        catch (JsonException)
        {
            buffer.Reset();
            return raw;
        }

        if (part is null || !string.Equals(part.Type, "part", StringComparison.OrdinalIgnoreCase)
            || part.N <= 0 || string.IsNullOrEmpty(part.B))
        {
            buffer.Reset();
            return raw;
        }

        return buffer.Add(part.I, part.N, part.B);
    }

    private static bool LooksLikePart(string raw)
    {
        var t = raw.AsSpan().TrimStart();
        if (t.Length < 10 || t[0] != '{')
            return false;
        return t.Contains("\"type\":\"part\"", StringComparison.Ordinal)
               || t.Contains("\"type\": \"part\"", StringComparison.Ordinal);
    }

    private sealed class FramePart
    {
        public int V { get; set; }
        public string? Type { get; set; }
        public int I { get; set; }
        public int N { get; set; }
        public string? B { get; set; }
    }
}

/// <summary>Per-peer reassembly of framed BLE parts.</summary>
public sealed class FrameReassembly
{
    private int _expected = -1;
    private string?[]? _slots;

    public string? Add(int index, int total, string base64)
    {
        if (total <= 0 || index < 0 || index >= total || string.IsNullOrEmpty(base64))
        {
            Reset();
            return null;
        }

        if (_slots is null || _expected != total)
        {
            _expected = total;
            _slots = new string[total];
        }

        _slots[index] = base64;

        if (_slots.Any(s => s is null))
            return null;

        try
        {
            var chunks = _slots.Select(s => Convert.FromBase64String(s!)).ToArray();
            var totalLen = chunks.Sum(c => c.Length);
            var buf = new byte[totalLen];
            var offset = 0;
            foreach (var chunk in chunks)
            {
                Buffer.BlockCopy(chunk, 0, buf, offset, chunk.Length);
                offset += chunk.Length;
            }

            Reset();
            return Encoding.UTF8.GetString(buf);
        }
        catch
        {
            Reset();
            return null;
        }
    }

    public void Reset()
    {
        _expected = -1;
        _slots = null;
    }
}
