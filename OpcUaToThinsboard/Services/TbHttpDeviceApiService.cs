using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using OpcUaToThinsboard.Models;

namespace OpcUaToThinsboard.Services;

public class TbHttpDeviceApiService : BackgroundService
{
    private readonly HttpClient _httpClient;

    public TbHttpDeviceApiService(IConfiguration configuration)
    {
        var url = configuration["Thingsboard:ServerUrl"]
            ?? throw new NullReferenceException($"Konfiguratsiyada Thingsboard:ServerUrl sozlanmagan!");

        _httpClient = new HttpClient()
        {
            BaseAddress = new Uri(url)
        };
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(100, stoppingToken);
        }
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
}
