namespace OpcUaToThinsboard.Models;

public record class HttpRpcTaskInfo(
    Task CheckTask,
    CancellationTokenSource CancellationTokenSource);