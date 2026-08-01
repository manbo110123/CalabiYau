using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

public sealed class UdpGameServerOptions
{
    public int ListenPort { get; set; } = 7777;
    public int TickRate { get; set; } = 30;
    public bool LogReceivedNetworkMessages { get; set; }
    public bool LogCommandDecisions { get; set; }
    public GameWorldSettings WorldSettings { get; set; } = new GameWorldSettings();
}

// Owns transport, routing, and the fixed-rate loop. It never changes PlayerState directly.
public sealed class UdpGameServer : IDisposable
{
    private readonly UdpGameServerOptions options;
    private readonly UdpClient udpServer;
    private readonly object stateLock = new object();
    private readonly ClientRegistry clientRegistry = new ClientRegistry();
    private readonly GameWorld world;
    private readonly SnapshotBuilder snapshotBuilder = new SnapshotBuilder();
    private long sentSnapshotCount;
    private long sentGameplayEventCount;

    public UdpGameServer(UdpGameServerOptions options)
    {
        this.options = options;
        udpServer = new UdpClient(options.ListenPort);
        world = new GameWorld(options.WorldSettings);
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        Console.WriteLine($"UDP server started on port {options.ListenPort}.");
        Console.WriteLine($"Server authority tick rate: {options.TickRate} Hz.");
        Console.WriteLine("Waiting for Unity ClientHello, PlayerInput and FireRequest messages...");

        Task receiveTask = ReceiveLoopAsync(cancellationToken);
        Task tickTask = TickLoopAsync(cancellationToken);

        await Task.WhenAll(receiveTask, tickTask);
    }

    public void Dispose()
    {
        udpServer.Dispose();
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            UdpReceiveResult received = await udpServer.ReceiveAsync(cancellationToken);
            string json = Encoding.UTF8.GetString(received.Buffer);
            string clientKey = ClientRegistry.GetClientKey(received.RemoteEndPoint);

            if (options.LogReceivedNetworkMessages)
            {
                Console.WriteLine();
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] From {clientKey}");
                Console.WriteLine(json);
            }

            string messageType = ReadMessageType(json);

            switch (messageType)
            {
                case "ClientHello":
                    await HandleClientHelloAsync(received.RemoteEndPoint, json);
                    break;

                case "PlayerInput":
                    HandlePlayerInput(received.RemoteEndPoint, json);
                    break;

                case "FireRequest":
                    await HandleFireRequestAsync(received.RemoteEndPoint, json);
                    break;

                case "":
                    Console.WriteLine("Message ignored: JSON is missing a readable type field.");
                    break;

                default:
                    Console.WriteLine($"Message ignored: unsupported type '{messageType}'.");
                    break;
            }
        }
    }

    private async Task TickLoopAsync(CancellationToken cancellationToken)
    {
        float tickDeltaTime = 1f / options.TickRate;
        using PeriodicTimer timer = new PeriodicTimer(TimeSpan.FromSeconds(tickDeltaTime));

        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            WorldSnapshotMessage snapshot;
            List<IPEndPoint> targets;
            List<GameWorldEvent> worldEvents;

            lock (stateLock)
            {
                worldEvents = world.Tick(tickDeltaTime);
                snapshot = snapshotBuilder.Build(world);
                targets = clientRegistry.GetAllEndpoints();
            }

            if (targets.Count > 0)
            {
                await BroadcastSnapshotAsync(snapshot, targets);
            }

            foreach (GameWorldEvent worldEvent in worldEvents)
            {
                await BroadcastGameplayEventAsync(CreateNetworkEvent(worldEvent), targets);
            }
        }
    }

    private async Task HandleClientHelloAsync(IPEndPoint remoteEndPoint, string json)
    {
        ClientHelloMessage? hello;

        try
        {
            hello = JsonSerializer.Deserialize<ClientHelloMessage>(json);
        }
        catch (JsonException exception)
        {
            Console.WriteLine($"ClientHello ignored: invalid JSON. {exception.Message}");
            return;
        }

        string playerName = string.IsNullOrWhiteSpace(hello?.Name) ? "Player" : hello.Name;
        ClientRegistration registration;

        lock (stateLock)
        {
            registration = clientRegistry.RegisterOrUpdate(playerName, remoteEndPoint);

            if (registration.IsNewClient)
            {
                world.AddPlayer(registration.Client.PlayerId);
                Console.WriteLine($"New client connected: {playerName}, playerId={registration.Client.PlayerId}");
            }
            else
            {
                Console.WriteLine($"Known client said hello again: {playerName}, playerId={registration.Client.PlayerId}");
            }
        }

        ServerWelcomeMessage welcome = new ServerWelcomeMessage
        {
            Type = "ServerWelcome",
            PlayerId = registration.Client.PlayerId,
            Message = "Welcome to the UDP demo server."
        };

        await SendJsonAsync(welcome, remoteEndPoint);
    }

    private void HandlePlayerInput(IPEndPoint remoteEndPoint, string json)
    {
        PlayerInputMessage? input;

        try
        {
            input = JsonSerializer.Deserialize<PlayerInputMessage>(json);
        }
        catch (JsonException exception)
        {
            Console.WriteLine($"PlayerInput ignored: invalid JSON. {exception.Message}");
            return;
        }

        if (input == null)
        {
            Console.WriteLine("PlayerInput ignored: empty message.");
            return;
        }

        lock (stateLock)
        {
            if (!clientRegistry.TryGetClient(remoteEndPoint, out ConnectedClient? client) || client == null)
            {
                Console.WriteLine("PlayerInput ignored: sender has not completed ClientHello.");
                return;
            }

            if (client.PlayerId != input.PlayerId)
            {
                Console.WriteLine($"PlayerInput ignored: endpoint owns playerId={client.PlayerId}, not {input.PlayerId}.");
                return;
            }

            InputCommand command = new InputCommand(
                input.InputTick,
                input.MoveAxis,
                input.TurnAxis,
                input.AimX,
                input.AimZ,
                input.Fire);

            CommandGateResult result = world.TryQueueInput(client.PlayerId, command);

            if (options.LogCommandDecisions || !result.IsAccepted)
            {
                Console.WriteLine(
                    $"PlayerInput {(result.IsAccepted ? "accepted" : "rejected")}: " +
                    $"playerId={client.PlayerId}, inputTick={command.InputTick}, reason={result.Reason}.");
            }
        }
    }

    private async Task HandleFireRequestAsync(IPEndPoint remoteEndPoint, string json)
    {
        FireRequestMessage? request;

        try
        {
            request = JsonSerializer.Deserialize<FireRequestMessage>(json);
        }
        catch (JsonException exception)
        {
            Console.WriteLine($"FireRequest ignored: invalid JSON. {exception.Message}");
            return;
        }

        if (request == null)
        {
            Console.WriteLine("FireRequest ignored: empty message.");
            return;
        }

        List<GameWorldEvent> eventsToBroadcast = new List<GameWorldEvent>();
        List<IPEndPoint> targets;
        string rejectReason = string.Empty;

        lock (stateLock)
        {
            if (!clientRegistry.TryGetClient(remoteEndPoint, out ConnectedClient? client) || client == null)
            {
                rejectReason = "sender has not completed ClientHello";
            }
            else if (client.PlayerId != request.PlayerId)
            {
                rejectReason = $"endpoint owns playerId={client.PlayerId}, not {request.PlayerId}";
            }
            else
            {
                FireCommand command = new FireCommand(
                    request.RequestTick,
                    request.AimX,
                    request.AimZ,
                    request.OriginX,
                    request.OriginY,
                    request.OriginZ,
                    request.DirectionX,
                    request.DirectionY,
                    request.DirectionZ,
                    request.EstimatedRttSeconds,
                    request.InterpolationDelaySeconds);

                bool accepted = world.TryHandleFireRequest(client.PlayerId, command, out eventsToBroadcast, out rejectReason);

                if (options.LogCommandDecisions || !accepted)
                {
                    Console.WriteLine(
                        $"FireRequest {(accepted ? "accepted" : "rejected")}: " +
                        $"playerId={client.PlayerId}, requestTick={command.RequestTick}, " +
                        $"reason={(accepted ? "resolved" : rejectReason)}.");
                }
            }

            targets = clientRegistry.GetAllEndpoints();
        }

        if (!string.IsNullOrEmpty(rejectReason))
        {
            Console.WriteLine($"FireRequest ignored: {rejectReason}.");
            return;
        }

        foreach (GameWorldEvent worldEvent in eventsToBroadcast)
        {
            await BroadcastGameplayEventAsync(CreateNetworkEvent(worldEvent), targets);
        }
    }

    private async Task BroadcastSnapshotAsync(WorldSnapshotMessage snapshot, List<IPEndPoint> targets)
    {
        string json = JsonSerializer.Serialize(snapshot);
        byte[] bytes = Encoding.UTF8.GetBytes(json);

        foreach (IPEndPoint target in targets)
        {
            await udpServer.SendAsync(bytes, bytes.Length, target);
        }

        sentSnapshotCount++;

        if (world.ServerTick % options.TickRate == 0)
        {
            Console.WriteLine(
                $"Tick={world.ServerTick}, players={snapshot.Players.Length}, " +
                $"input={world.AcceptedInputCount}/{world.ReceivedInputCount}, inputRejected={world.RejectedInputCount}, " +
                $"inputSuperseded={world.SupersededInputCount}, inputReasons=[{world.GetInputRejectionSummary()}], " +
                $"snapshots={sentSnapshotCount}, fire={world.AcceptedFireRequestCount}/{world.ReceivedFireRequestCount}, " +
                $"fireRejected={world.RejectedFireRequestCount}, fireReasons=[{world.GetFireRejectionSummary()}], " +
                $"events={sentGameplayEventCount}, lagCompFire={world.LagCompensatedFireRequestCount}, " +
                $"deaths={world.DeathCount}, respawns={world.RespawnCount}");
        }
    }

    private async Task BroadcastGameplayEventAsync(object message, List<IPEndPoint> targets)
    {
        string json = JsonSerializer.Serialize(message, message.GetType());
        byte[] bytes = Encoding.UTF8.GetBytes(json);

        foreach (IPEndPoint target in targets)
        {
            await udpServer.SendAsync(bytes, bytes.Length, target);
        }

        sentGameplayEventCount++;
        Console.WriteLine($"Broadcast gameplay event: {json}");
    }

    private async Task SendJsonAsync<TMessage>(TMessage message, IPEndPoint target)
    {
        string json = JsonSerializer.Serialize(message);
        byte[] bytes = Encoding.UTF8.GetBytes(json);
        await udpServer.SendAsync(bytes, bytes.Length, target);

        Console.WriteLine($"Sent to {ClientRegistry.GetClientKey(target)}");
        Console.WriteLine(json);
    }

    private static string ReadMessageType(string json)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);

            if (document.RootElement.TryGetProperty("type", out JsonElement typeElement))
            {
                return typeElement.GetString() ?? string.Empty;
            }
        }
        catch (JsonException exception)
        {
            Console.WriteLine($"Invalid JSON: {exception.Message}");
        }

        return string.Empty;
    }

    private static object CreateNetworkEvent(GameWorldEvent worldEvent)
    {
        switch (worldEvent)
        {
            case FireResolvedEvent fireEvent:
                return new FireEventMessage
                {
                    Type = "FireEvent",
                    ServerTick = fireEvent.ServerTick,
                    ShooterPlayerId = fireEvent.ShooterPlayerId,
                    RequestTick = fireEvent.RequestTick,
                    OriginX = fireEvent.OriginX,
                    OriginY = fireEvent.OriginY,
                    OriginZ = fireEvent.OriginZ,
                    DirectionX = fireEvent.DirectionX,
                    DirectionY = fireEvent.DirectionY,
                    DirectionZ = fireEvent.DirectionZ,
                    Range = fireEvent.Range,
                    LagCompensated = fireEvent.LagCompensated,
                    HitTestServerTick = fireEvent.HitTestServerTick,
                    RewindSeconds = fireEvent.RewindSeconds
                };

            case HitResolvedEvent hitEvent:
                return new HitEventMessage
                {
                    Type = "HitEvent",
                    ServerTick = hitEvent.ServerTick,
                    ShooterPlayerId = hitEvent.ShooterPlayerId,
                    TargetPlayerId = hitEvent.TargetPlayerId,
                    HitX = hitEvent.HitX,
                    HitY = hitEvent.HitY,
                    HitZ = hitEvent.HitZ,
                    Damage = hitEvent.Damage
                };

            case HealthChangedWorldEvent healthEvent:
                return new HealthChangedEventMessage
                {
                    Type = "HealthChangedEvent",
                    ServerTick = healthEvent.ServerTick,
                    PlayerId = healthEvent.PlayerId,
                    Health = healthEvent.Health,
                    MaxHealth = healthEvent.MaxHealth,
                    IsAlive = healthEvent.IsAlive,
                    RespawnRemainingSeconds = healthEvent.RespawnRemainingSeconds
                };

            default:
                throw new InvalidOperationException($"Unsupported world event '{worldEvent.GetType().Name}'.");
        }
    }
}
