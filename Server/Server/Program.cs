GameWorldSettings worldSettings = new GameWorldSettings();
bool useFullSnapshots = args.Any(argument => string.Equals(argument, "--full-snapshots", StringComparison.OrdinalIgnoreCase));
bool enableDistanceFiltering = !useFullSnapshots;

UdpGameServerOptions serverOptions = new UdpGameServerOptions
{
    ListenPort = 7777,
    TickRate = worldSettings.ServerTickRate,
    LogReceivedNetworkMessages = false,
    WorldSettings = worldSettings,
    ReplicationSettings = new ClientReplicationSettings
    {
        // Distance filtering is the phase-11 default. Pass --full-snapshots for a direct
        // chapter-one baseline comparison without changing source code.
        EnableDistanceFiltering = enableDistanceFiltering,
        // Keep owner/high-priority confirmation at the 30 Hz simulation rate so local
        // prediction has the same acknowledgement cadence as the chapter-one baseline.
        SnapshotRate = 30,
        HighPrioritySnapshotRate = 30,
        LowPrioritySnapshotRate = 5
    }
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
