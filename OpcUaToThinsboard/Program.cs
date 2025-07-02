using OpcUaToThinsboard;
using OpcUaToThinsboard.Services;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddSingleton<TbHttpDeviceApiService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<TbHttpDeviceApiService>());
builder.Services.AddHostedService<OpcUaClientService>();

var host = builder.Build();
host.Run();
