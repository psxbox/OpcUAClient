using System;
using System.Text.Json.Serialization;

namespace OpcUaToThinsboard.Models;

public class TelemetryPayload
{
    [JsonPropertyName("ts")]
    public long Ts { get; set; }

    [JsonPropertyName("values")]
    public Dictionary<string, object> Values { get; set; } = new();
}
