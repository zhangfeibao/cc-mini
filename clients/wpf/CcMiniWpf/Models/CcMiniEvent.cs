using System.Text.Json;
using System.Text.Json.Serialization;

namespace CcMiniWpf.Models;

/// <summary>
/// 从 cc-mini --stdio 接收的 NDJSON 事件。
/// </summary>
public sealed class CcMiniEvent
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("event")]
    public string Event { get; set; } = "";

    [JsonPropertyName("data")]
    public JsonElement Data { get; set; }

    // ── 便捷访问器 ──

    public string GetString(string key, string fallback = "")
    {
        if (Data.ValueKind != JsonValueKind.Object) return fallback;
        return Data.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? fallback
            : fallback;
    }

    public int GetInt(string key, int fallback = 0)
    {
        if (Data.ValueKind != JsonValueKind.Object) return fallback;
        return Data.TryGetProperty(key, out var v) && v.TryGetInt32(out var n) ? n : fallback;
    }

    public bool GetBool(string key, bool fallback = false)
    {
        if (Data.ValueKind != JsonValueKind.Object) return fallback;
        if (!Data.TryGetProperty(key, out var v)) return fallback;
        return v.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => fallback,
        };
    }

    public JsonElement? GetElement(string key)
    {
        if (Data.ValueKind != JsonValueKind.Object) return null;
        return Data.TryGetProperty(key, out var v) ? v : null;
    }
}
