using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
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
        private Session? session;
        private List<Device>? devices;

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            logger.LogInformation("OPC UA Client Service is starting...");

            var serverUrl = configuration["OPCUA:ServerUrl"]
                ?? throw new Exception("Konfiguratsiyada OPCUA:ServerUrl sozlanmagam!");

            session = await CreateSession(serverUrl, stoppingToken);
            await LoadDevicesAsync(stoppingToken);
            await PrepareDevicesAsync(stoppingToken);
            logger.LogInformation("OPC UA Client Service started successfully.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    if (!session.Connected)
                    {
                        logger.LogInformation("Reconnecting to OPC UA server...");
                        try
                        {
                            await session.ReconnectAsync(stoppingToken);
                            logger.LogInformation("Reconnected to OPC UA server successfully.");
                        }
                        catch (Exception ex)
                        {
                            logger.LogError(ex, "Failed to reconnect to OPC UA server.");
                        }
                    }

                    await Task.Delay(1000, stoppingToken);
                }
                catch (TaskCanceledException)
                {
                    logger.LogInformation("OPC UA Client Service is stopping...");
                    break;
                }
            }
        }

        private async Task PrepareDevicesAsync(CancellationToken cancellationToken)
        {
            logger.LogInformation("Preparing devices...");

            if (session == null)
            {
                throw new Exception("OPC UA sessiyasi ochilmadi!");
            }
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

        private async Task StartHistoriesReadTasks(Device device, CancellationToken cancellationToken)
        {
            
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

                while (!cancellationToken.IsCancellationRequested)
                {
                    Stopwatch stopwatch = Stopwatch.StartNew();
                    // read nodes every interval
                    try
                    {
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

        private async Task<Session> CreateSession(string serverUrl, CancellationToken cancellationToken)
        {
            logger.LogInformation("Initializing OPC UA application...");

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

        public override void Dispose()
        {
            GC.SuppressFinalize(this);
            session?.Dispose();
            base.Dispose();
        }
    }
}
