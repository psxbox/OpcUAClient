using System;

namespace OpcUaToThinsboard.Models;

public class ServerSideRpcRequest
{
    public int Id { get; set; }
    public string Method { get; set; } = string.Empty;
    public object Params { get; set; } = new object();
}
