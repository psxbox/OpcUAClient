using System;

namespace OpcUaToThinsboard.Models;

public class TelemetryPayload
{
    public ulong Ts { get; set; }
    public Dictionary<string, object> Values { get; set; } = [];
}
