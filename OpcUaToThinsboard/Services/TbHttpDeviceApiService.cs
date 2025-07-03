using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using OpcUaToThinsboard.Models;

namespace OpcUaToThinsboard.Services;

public class TbHttpDeviceApiService : BackgroundService
{
    private readonly HttpClient _httpClient;
    private readonly ConcurrentDictionary<string, HttpRpcTaskInfo> _deviceTokens = new();
    private readonly ILogger<TbHttpDeviceApiService> logger;
    private CancellationToken _token;

    public TbHttpDeviceApiService(IConfiguration configuration, ILogger<TbHttpDeviceApiService> logger)
    {
        var url = configuration["Thingsboard:ServerUrl"]
            ?? throw new NullReferenceException($"Konfiguratsiyada Thingsboard:ServerUrl sozlanmagan!");

        _httpClient = new HttpClient()
        {
            BaseAddress = new Uri(url)
        };
        this.logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _token = stoppingToken;

        while (!stoppingToken.IsCancellationRequested)
        {
            // await HandleRpcRequests(stoppingToken);

            await Task.Delay(100, stoppingToken);
        }

    }

    private async Task ServerSideRpcRequestHandler(string deviceToken,
                                                   Func<string, ServerSideRpcRequest, CancellationToken, Task> handler,
                                                   CancellationToken cancellationToken)
    {
        var endpoint = $"/api/v1/{deviceToken}/rpc";
        logger.LogInformation("Starting server-side RPC request handler for device token: {DeviceToken}", deviceToken);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var response = await _httpClient.GetFromJsonAsync<ServerSideRpcRequest>(endpoint, cancellationToken);
                if (response != null)
                {
                    await handler(deviceToken, response, cancellationToken);
                }
            }
            catch (HttpRequestException ex)
            {
                if (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    logger.LogWarning("Endpoint {Endpoint} not found. Stopping handler.", endpoint);
                }

                if (ex.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    logger.LogWarning("Unauthorized access to endpoint {Endpoint}. Check your credentials.", endpoint);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error handling server-side RPC request for endpoint {Endpoint}", endpoint);
            }
        }
    }

    public async Task SendRpcResponseAsync(string deviceToken, int id, object response)
    {
        var json = JsonSerializer.Serialize(response);
        var endpoint = $"/api/v1/{deviceToken}/rpc/{id}";
        await SendContentAsync(endpoint, json);
    }

    public async Task SendAttributesAsync(string deviceToken, Dictionary<string, object> attributes)
    {
        var json = JsonSerializer.Serialize(attributes);
        var endpoint = $"/api/v1/{deviceToken}/attributes";
        await SendContentAsync(endpoint, json);
    }

    public async Task SendTelemetryAsync(string deviceToken, List<Dictionary<string, object>> telemetry)
    {
        var json = JsonSerializer.Serialize(telemetry);
        var endpoint = $"/api/v1/{deviceToken}/telemetry";
        await SendContentAsync(endpoint, json);
    }

    public async Task SendTelemetryAsync(string deviceToken, TelemetryPayload telemetry)
    {
        var json = JsonSerializer.Serialize(telemetry);
        var endpoint = $"/api/v1/{deviceToken}/telemetry";
        await SendContentAsync(endpoint, json);
    }

    public async Task SendTelemetryAsync(string deviceToken, List<TelemetryPayload> telemetry)
    {
        var json = JsonSerializer.Serialize(telemetry);
        var endpoint = $"/api/v1/{deviceToken}/telemetry";
        await SendContentAsync(endpoint, json);
    }

    private async Task SendContentAsync(string endpoint, string content)
    {
        var stringContent = new StringContent(content, Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync(endpoint, stringContent);

        if (!response.IsSuccessStatusCode)
        {
            throw new Exception($"Failed to send content to {endpoint}. Status code: {response.StatusCode}");
        }
    }

    public async Task<AttributesResponse> GetAttributesAsync(string deviceToken, IEnumerable<string>? clientAttributes = null, IEnumerable<string>? sharedAttributes = null)
    {
        var endpoint = $"/api/v1/{deviceToken}/attributes?clientKeys={string.Join(",", clientAttributes ?? [])}&sharedKeys={string.Join(",", sharedAttributes ?? [])}";
        var response = await _httpClient.GetFromJsonAsync<AttributesResponse>(endpoint)
            ?? throw new Exception($"Failed to get attributes from {endpoint}. Response was null.");
        return response;
    }

    public void RegisterDeviceToken(string deviceToken, Func<string, ServerSideRpcRequest, CancellationToken, Task> handler)
    {
        if (string.IsNullOrWhiteSpace(deviceToken))
        {
            throw new ArgumentException("Device token cannot be null or empty.", nameof(deviceToken));
        }

        if (_deviceTokens.ContainsKey(deviceToken))
        {
            logger.LogWarning("Device token {DeviceToken} is already registered.", deviceToken);
            return;
        }

        var taskCts = CancellationTokenSource.CreateLinkedTokenSource(_token);  

        var checkTask = Task.Run(() => ServerSideRpcRequestHandler(deviceToken, handler, taskCts.Token), taskCts.Token);

        _deviceTokens[deviceToken] = new HttpRpcTaskInfo(
            CheckTask: checkTask,
            CancellationTokenSource: taskCts
        );
    }
}
