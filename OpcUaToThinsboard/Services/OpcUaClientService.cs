using System.Diagnostics;
using System.Text.Json;
using Cronos;
using Opc.Ua;
using Opc.Ua.Client;
using OpcUaToThinsboard.Models;

namespace OpcUaToThinsboard.Services
{
    public class OpcUaClientService(
        TbHttpDeviceApiService tbHttpDeviceApiService,
        IConfiguration configuration,
        ILogger<OpcUaClientService> logger) : BackgroundService
    {
        private List<Device>? devices;
        private ApplicationConfiguration? config;

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            logger.LogInformation("OPC UA Client Service is starting...");

            await LoadDevicesAsync(stoppingToken);
            await PrepareDevicesAsync(stoppingToken);
            config = await GetConfiguration();

            logger.LogInformation("OPC UA Client Service started successfully.");

            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(1000, stoppingToken);
            }
        }

        private async Task PrepareDevicesAsync(CancellationToken cancellationToken)
        {
            logger.LogInformation("Preparing devices...");

            if (devices == null || devices.Count == 0)
            {
                throw new Exception("Qurilmalar yuklanmadi yoki bo'sh!");
            }

            foreach (var device in devices)
            {
                // subscribe to device data changes
                bool flowControl = await StartTasks(device, cancellationToken);
                if (!flowControl)
                {
                    continue;
                }
            }
        }

        private async Task<bool> StartTasks(Device device, CancellationToken cancellationToken)
        {
            await StartSubscriptions(device, cancellationToken);
            await StartHistoriesReadTasks(device, cancellationToken);

            return true;
        }

        private Task StartHistoriesReadTasks(Device device, CancellationToken cancellationToken)
        {
            if (device.Histories == null || device.Histories.Count == 0)
            {
                return Task.CompletedTask;
            }

            foreach (var history in device.Histories)
            {
                if (string.IsNullOrWhiteSpace(history.CheckCron))
                {
                    continue;
                }

                var cronExpression = CronExpression.Parse(history.CheckCron);

                _ = Task.Run(async () =>
                {
                    DateTimeOffset? next = cronExpression.GetNextOccurrence(DateTimeOffset.Now, TimeZoneInfo.Local);
                    while (!cancellationToken.IsCancellationRequested && next != null)
                    {
                        var delay = next.Value - DateTimeOffset.Now;
                        if (delay > TimeSpan.Zero)
                        {
                            await Task.Delay(delay, cancellationToken);
                        }

                        try
                        {
                            logger.LogInformation("[{device.Name}] History '{history.Name}' task triggered by cron: {history.CheckCron}",
                                device.Name, history.Name, history.CheckCron);

                            var lastRaded = await tbHttpDeviceApiService.GetAttributesAsync(device.Token,
                                    [$"lastRead_{history.Name}"]);

                            var now = DateTime.Now;

                            DateTime startTime, endTime;
                            switch (history.HistoryType?.ToLowerInvariant())
                            {
                                case "daily":
                                    startTime = new DateTime(now.Year, now.Month, now.Day).AddDays(-30);
                                    endTime = new DateTime(now.Year, now.Month, now.Day, 0, 0, 0);
                                    break;
                                case "hourly":
                                    startTime = new DateTime(now.Year, now.Month, now.Day).AddDays(-2);
                                    endTime = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0);
                                    break;
                                default:
                                    throw new Exception($"Unknown history type: {history.HistoryType}");
                            }

                            if (!lastRaded.Client.TryGetValue($"lastRead_{history.Name}", out var value) && value is not null)
                            {
                                startTime = DateTime.Parse(value.ToString()!);
                            }

                            var telemetryData = await ReadHistoryAsync(device, history, startTime, endTime, cancellationToken);

                            if (telemetryData != null)
                            {
                                await tbHttpDeviceApiService.SendTelemetryAsync(device.Token, telemetryData);
                            }
                        }
                        catch (Exception ex)
                        {
                            logger.LogError(ex, "[{device.Name}] History '{history.Name}' read task error",
                                device.Name, history.Name);
                        }

                        next = cronExpression.GetNextOccurrence(DateTimeOffset.Now, TimeZoneInfo.Local);
                    }
                }, cancellationToken);
            }
            return Task.CompletedTask;
        }

        private async Task<List<TelemetryPayload>?> ReadHistoryAsync(Device device, History history,
            DateTime startTime, DateTime endTime, CancellationToken cancellationToken)
        {
            if (startTime > endTime)
            {
                logger.LogWarning("Start time {startTime} is after end time {endTime} for history {history.Name}. Skipping read.",
                    startTime, endTime, history.Name);
                return null;
            }

            HistoryReadValueId historyReadId = new HistoryReadValueId
            {
                NodeId = new NodeId(history.NodeId),
                IndexRange = null,
                DataEncoding = null
            };

            ReadRawModifiedDetails details = new ReadRawModifiedDetails
            {
                IsReadModified = false,
                StartTime = startTime,
                EndTime = endTime,
                NumValuesPerNode = 200,
                ReturnBounds = true
            };

            var collection = new HistoryReadValueIdCollection([historyReadId]);
            using var session = await CreateSession(cancellationToken);

            for (int attempt = 1; attempt <= 2; attempt++)
            {
                var response = await session!.HistoryReadAsync(
                    null,
                    new ExtensionObject(details),
                    TimestampsToReturn.Both,
                    false,
                    collection,
                    CancellationToken.None);

                if (StatusCode.IsGood(response.Results[0].StatusCode))
                {
                    var telemetry = new List<TelemetryPayload>();

                    HistoryData historyData = (HistoryData)ExtensionObject.ToEncodeable(response.Results[0].HistoryData);
                    foreach (var dataValue in historyData.DataValues)
                    {
                        var dt = dataValue.SourceTimestamp.ToLocalTime();
                        logger.LogDebug("Время: {dt}, Значение: {value}", dt, dataValue.Value);

                        telemetry.Add(new TelemetryPayload
                        {
                            Ts = new DateTimeOffset(dt).ToUnixTimeMilliseconds(),
                            Values = new Dictionary<string, object>
                            {
                                { history.Name, dataValue.Value }
                            }
                        });
                    }
                    return telemetry;
                }
                else
                {
                    logger.LogError("Ошибка чтения архивных данных (попытка {attempt}): {statusCode}", attempt, response.Results[0].StatusCode);
                    await Task.Delay(1000, cancellationToken);
                }
            }
            return null;
        }

        private Task StartSubscriptions(Device device, CancellationToken cancellationToken)
        {
            if (device.Subscriptions == null || device.Subscriptions.Tags.Count == 0)
            {
                return Task.CompletedTask;
            }

            _ = Task.Run(async () =>
            {
                List<NodeId> readValueIds = [];
                foreach (var tag in device.Subscriptions.Tags)
                {
                    readValueIds.Add(tag.NodeId);
                }

                var attributes = new Dictionary<string, object>();

                Stopwatch stopwatch = new();
                while (!cancellationToken.IsCancellationRequested)
                {
                    // read nodes every interval
                    stopwatch.Restart();
                    try
                    {
                        using var session = await CreateSession(cancellationToken);

                        var (dataCollection, serviceResults) = await session!.ReadValuesAsync(readValueIds, cancellationToken);

                        attributes.Clear();

                        for (int i = 0; i < device.Subscriptions.Tags.Count; i++)
                        {
                            Tag tag = device.Subscriptions.Tags[i];
                            var data = dataCollection[i];
                            if (StatusCode.IsGood(data.StatusCode))
                            {
                                attributes[tag.Name] = data.Value;
                            }
                            else
                            {
                                logger.LogWarning("Failed to read node {nodeId}: {statusCode}", tag.NodeId, serviceResults[i]);
                            }
                        }

                        logger.LogDebug("Device Name: {deviceName} Result: {result}", device.Name, attributes);

                        await tbHttpDeviceApiService.SendAttributesAsync(device.Token, attributes);
                    }
                    catch (ServiceResultException ex)
                    {
                        logger.LogError(ex, "Server error while reading OPC UA nodes: {message}", ex.Message);
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Error reading OPC UA nodes.");
                    }

                    stopwatch.Stop();
                    var wait = device.Subscriptions.Interval - (int)stopwatch.ElapsedMilliseconds;
                    if (wait > 0)
                    {
                        await Task.Delay(wait, cancellationToken);
                    }
                }
            }, cancellationToken);

            return Task.CompletedTask;
        }

        private async Task LoadDevicesAsync(CancellationToken cancellationToken)
        {
            logger.LogInformation("Loading devices from JSON...");
            if (!File.Exists("data/devices.json"))
            {
                throw new FileNotFoundException("devices.json fayli topilmadi!");
            }

            var devicesJson = await File.ReadAllTextAsync("data/devices.json", cancellationToken);
            devices = JsonSerializer.Deserialize<List<Device>>(devicesJson, JsonSerializerOptions.Web)
                ?? [];

            if (devices.Count == 0)
            {
                throw new Exception("Devices.json faylida qurilmalar topilmadi!");
            }
        }

        private async Task<Session> CreateSession(CancellationToken cancellationToken)
        {
            logger.LogInformation("Initializing OPC UA session...");

            var serverUrl = configuration["OPCUA:ServerUrl"];

            if (string.IsNullOrEmpty(serverUrl))
            {
                throw new Exception("Konfiguratsiyada OPCUA:ServerUrl sozlanmagam!");
            }

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var selectedEndpoint = CoreClientUtils.SelectEndpoint(config, serverUrl, useSecurity: false);
                    var endpointConfig = EndpointConfiguration.Create(config);
                    var endpoint = new ConfiguredEndpoint(null, selectedEndpoint, endpointConfig);

                    var session = await Session.Create(config, endpoint, false, "OPC UA Client", 60000,
                                                   null, null, cancellationToken);
                    return session;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to create OPC UA session. Retrying in 5 seconds...");
                    await Task.Delay(5000, cancellationToken);
                }
            }
            throw new Exception("OPC UA sessiyasi yaratilmadi!");
        }

        private async Task<ApplicationConfiguration> GetConfiguration()
        {
            var config = new ApplicationConfiguration
            {
                ApplicationName = "OPC UA Client",
                ApplicationType = ApplicationType.Client,
                SecurityConfiguration = new SecurityConfiguration
                {
                    AutoAcceptUntrustedCertificates = true,
                    ApplicationCertificate = new CertificateIdentifier(),
                    TrustedIssuerCertificates = new CertificateTrustList { StoreType = "Directory", StorePath = "pki/issuers" },
                    TrustedPeerCertificates = new CertificateTrustList { StoreType = "Directory", StorePath = "pki/trusted" },
                    RejectedCertificateStore = new CertificateTrustList { StoreType = "Directory", StorePath = "pki/rejected" }
                },
                TransportConfigurations = [],
                TransportQuotas = new TransportQuotas { OperationTimeout = 15000 },
                ClientConfiguration = new ClientConfiguration { DefaultSessionTimeout = 60000, },
            };

            await config.Validate(ApplicationType.Client);
            return config;
        }

        public override void Dispose()
        {
            GC.SuppressFinalize(this);
            base.Dispose();
        }
    }
}
