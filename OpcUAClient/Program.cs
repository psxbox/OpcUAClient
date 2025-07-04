using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Configuration;

var config = new ApplicationConfiguration
{
    ApplicationName = "OpcUaClient",
    ApplicationType = ApplicationType.Client,
    SecurityConfiguration = new SecurityConfiguration
    {
        AutoAcceptUntrustedCertificates = true,
        ApplicationCertificate = new CertificateIdentifier(),
        TrustedIssuerCertificates = new CertificateTrustList { StoreType = "Directory", StorePath = "pki/issuers" },
        TrustedPeerCertificates = new CertificateTrustList { StoreType = "Directory", StorePath = "pki/trusted" },
        RejectedCertificateStore = new CertificateTrustList { StoreType = "Directory", StorePath = "pki/rejected" }
    },
    TransportConfigurations = new TransportConfigurationCollection(),
    TransportQuotas = new TransportQuotas { OperationTimeout = 15000 },
    ClientConfiguration = new ClientConfiguration { DefaultSessionTimeout = 60000 },
};

await config.Validate(ApplicationType.Client);
var app = new ApplicationInstance { ApplicationName = "OpcUaClient", ApplicationConfiguration = config };

//await app.CheckApplicationInstanceCertificate(false, 0);

// URL сервера, например: "opc.tcp://localhost:4840"
var selectedEndpoint = CoreClientUtils.SelectEndpoint(config, "opc.tcp://192.168.50.193:37112/LogikaUA", useSecurity: false);
var endpointConfig = EndpointConfiguration.Create(config);
var endpoint = new ConfiguredEndpoint(null, selectedEndpoint, endpointConfig);

var session = await Session.Create(config, endpoint, false, "OPC UA Client", 60000, null, null);


// NodeId — это уникальный идентификатор узла (например, "ns=2;s=MyVariable")
var nodeToRead = new ReadValueId
{
    NodeId = new NodeId("ns=3;s=/СПСеть/ГАЗ/FIC_3471 ГАЗ/FIC_3471 Давления ГАЗ (МПа)"),
    AttributeId = Attributes.Value,
};

var nodesToRead = new ReadValueIdCollection { nodeToRead };
var response = await session.ReadAsync(null, 0, TimestampsToReturn.Both, nodesToRead, CancellationToken.None);

var value = response.Results[0].Value;
Console.WriteLine($"Значение: {value}");

await HistoryRead(session);

async Task HistoryRead(Session session)
{
    // Предполагается, что у вас уже создана сессия
    HistoryReadValueId historyReadId = new HistoryReadValueId
    {
        NodeId = new NodeId("ns=3;s=/СПСеть/ГАЗ/Янгги котелни ГАЗ/Янгги котелни СУТОЧНЫЙ АРХИВ"), 
        IndexRange = null,
        DataEncoding = null
    };

    ReadRawModifiedDetails details = new ReadRawModifiedDetails
    {
        IsReadModified = false,
        StartTime = new DateTime(2025, 6, 1),  // начало диапазона
        EndTime = new DateTime(2025, 6, 30),    // конец диапазона
        NumValuesPerNode = 100,                 // макс. кол-во значений
        ReturnBounds = true
    };


    var collection = new HistoryReadValueIdCollection([historyReadId]);

    var response = await session.HistoryReadAsync(
        null,
        new ExtensionObject(details),
        TimestampsToReturn.Both,
        false,
        collection,
        CancellationToken.None);

    if (StatusCode.IsGood(response.Results[0].StatusCode))
    {
        HistoryData historyData = (HistoryData)ExtensionObject.ToEncodeable(response.Results[0].HistoryData);
        foreach (var value in historyData.DataValues)
        {
            var dt = value.SourceTimestamp.ToLocalTime();

            Console.WriteLine($"Время: {dt}, Значение: {value.Value}");
        }
    }
    else
    {
        Console.WriteLine("Ошибка чтения архивных данных: " + response.Results[0].StatusCode);
    }
}