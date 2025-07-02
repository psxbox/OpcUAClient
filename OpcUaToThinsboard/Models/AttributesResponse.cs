using System;

namespace OpcUaToThinsboard.Models;

public class AttributesResponse
{
    public Dictionary<string, object> Client { get; set; } = [];
    public Dictionary<string, object> Shared { get; set; } = [];
}
