using System;

namespace OpcUaToThinsboard.Models;

public class TelemetryPayload
{
    public long Ts { get; set; }
    public Dictionary<string, object> Values { get; set; } = [];
}
