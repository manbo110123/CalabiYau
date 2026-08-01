GameWorldSettings worldSettings = new GameWorldSettings();

UdpGameServerOptions serverOptions = new UdpGameServerOptions
{
    ListenPort = 7777,
    TickRate = worldSettings.ServerTickRate,
    LogReceivedNetworkMessages = false,
    WorldSettings = worldSettings
};

using CancellationTokenSource shutdown = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    shutdown.Cancel();
};

using UdpGameServer server = new UdpGameServer(serverOptions);

try
{
    await server.RunAsync(shutdown.Token);
}
catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
{
    Console.WriteLine("UDP server stopped.");
}
