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
    // UDP has no disconnect signal. This bounds how long a silent client can keep an
    // authoritative player and a remote avatar alive after a crash or closed game.
    public float ClientTimeoutSeconds { get; set; } = 8f;
    public GameWorldSettings WorldSettings { get; set; } = new GameWorldSettings();
    public ClientReplicationSettings ReplicationSettings { get; set; } = new ClientReplicationSettings();
    public ReliableEventSettings ReliableEventSettings { get; set; } = new ReliableEventSettings();
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
    private long sentReliableEventDatagramCount;
    private long nextReliableEventId;
    private float snapshotSendAccumulator;

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
        Console.WriteLine($"Per-client snapshot rate: {GetSnapshotRate()} Hz, distance filtering: {options.ReplicationSettings.EnableDistanceFiltering}.");
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

            if (messageType != "ClientHello")
            {
                lock (stateLock)
                {
                    clientRegistry.Touch(received.RemoteEndPoint, DateTime.UtcNow);
                }
            }

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

                case "EventAck":
                    HandleEventAck(received.RemoteEndPoint, json);
                    break;

                case "ClientGoodbye":
                    HandleClientGoodbye(received.RemoteEndPoint);
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
            List<SnapshotSendWork> snapshotSendWork = new List<SnapshotSendWork>();
            List<UnreliableEventSendWork> unreliableEventSendWork = new List<UnreliableEventSendWork>();
            List<ReliableEventSendWork> reliableEventSendWork = new List<ReliableEventSendWork>();
            List<GameWorldEvent> worldEvents;

            lock (stateLock)
            {
                List<ConnectedClient> timedOutClients = clientRegistry.RemoveInactive(
                    DateTime.UtcNow,
                    TimeSpan.FromSeconds(Math.Max(1f, options.ClientTimeoutSeconds)));

                foreach (ConnectedClient timedOutClient in timedOutClients)
                {
                    world.RemovePlayer(timedOutClient.PlayerId);
                    Console.WriteLine($"Client timed out: playerId={timedOutClient.PlayerId}, name={timedOutClient.Name}.");
                }

                worldEvents = world.Tick(tickDeltaTime);
                IReadOnlyList<ReplicationCandidate> candidates = snapshotBuilder.Capture(world);
                DateTime nowUtc = DateTime.UtcNow;

                foreach (ConnectedClient client in clientRegistry.GetAllClients())
                {
                    client.ReliableEvents.DiscardOutsideReplicationScope(pending =>
                        IsEventRelevantToClient(client, pending.RelatedPlayerIds, candidates));

                    foreach (PendingReliableEvent pending in client.ReliableEvents.CollectDueResends(nowUtc))
                    {
                        reliableEventSendWork.Add(new ReliableEventSendWork(client.RemoteEndPoint, pending.Json));
                    }
                }

                foreach (GameWorldEvent worldEvent in worldEvents)
                {
                    object networkEvent = CreateNetworkEvent(worldEvent);
                    int[] relatedPlayerIds = GetRelatedPlayerIds(worldEvent);
                    bool isReliable = IsReliableWorldEvent(worldEvent);
                    string eventJson = JsonSerializer.Serialize(networkEvent, networkEvent.GetType());

                    if (isReliable)
                    {
                        long eventId = ++nextReliableEventId;
                        SetReliableEventId(networkEvent, eventId);
                        eventJson = JsonSerializer.Serialize(networkEvent, networkEvent.GetType());

                        foreach (ConnectedClient client in clientRegistry.GetAllClients())
                        {
                            // FireResult is a private final answer to the player who made
                            // this request. It still uses that client's normal reliable
                            // ledger, but must never be replicated to observers.
                            if (worldEvent is FireResultWorldEvent fireResult
                                && client.PlayerId != fireResult.ShooterPlayerId)
                            {
                                continue;
                            }

                            if (!IsEventRelevantToClient(client, relatedPlayerIds, candidates))
                            {
                                continue;
                            }

                            client.ReliableEvents.QueueInitial(eventId, eventJson, worldEvent.ServerTick, relatedPlayerIds, nowUtc);
                            reliableEventSendWork.Add(new ReliableEventSendWork(client.RemoteEndPoint, eventJson));
                        }
                    }
                    else
                    {
                        foreach (ConnectedClient client in clientRegistry.GetAllClients())
                        {
                            if (IsEventRelevantToClient(client, relatedPlayerIds, candidates))
                            {
                                unreliableEventSendWork.Add(new UnreliableEventSendWork(client.RemoteEndPoint, eventJson));
                            }
                        }
                    }
                }

                snapshotSendAccumulator += tickDeltaTime;
                float snapshotIntervalSeconds = 1f / GetSnapshotRate();

                if (snapshotSendAccumulator >= snapshotIntervalSeconds)
                {
                    snapshotSendAccumulator -= snapshotIntervalSeconds;
                    foreach (ConnectedClient client in clientRegistry.GetAllClients())
                    {
                        ClientSnapshotPlan plan = client.Replicator.BuildSnapshot(
                            client.PlayerId,
                            world.ServerTick,
                            options.TickRate,
                            candidates);
                        snapshotSendWork.Add(new SnapshotSendWork(client, plan));
                    }
                }
            }

            foreach (SnapshotSendWork work in snapshotSendWork)
            {
                await SendSnapshotAsync(work);
            }

            foreach (UnreliableEventSendWork work in unreliableEventSendWork)
            {
                await SendRawJsonAsync(work.Json, work.Target);
                sentGameplayEventCount++;
            }

            foreach (ReliableEventSendWork work in reliableEventSendWork)
            {
                await SendRawJsonAsync(work.Json, work.Target);
                sentReliableEventDatagramCount++;
            }

            if (world.ServerTick % Math.Max(1, options.TickRate) == 0)
            {
                LogServerTelemetry();
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
            registration = clientRegistry.RegisterOrUpdate(playerName, remoteEndPoint, options.ReplicationSettings, options.ReliableEventSettings);

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
            ServerTickRate = options.TickRate,
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
                input.AimZ);

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

        FireReceiptMessage? receipt = null;

        lock (stateLock)
        {
            if (!clientRegistry.TryGetClient(remoteEndPoint, out ConnectedClient? client) || client == null)
            {
                Console.WriteLine("FireRequest ignored: sender has not completed ClientHello.");
                return;
            }

            if (client.PlayerId != request.PlayerId)
            {
                Console.WriteLine($"FireRequest ignored: endpoint owns playerId={client.PlayerId}, not {request.PlayerId}.");
                return;
            }

            FireCommand command = new FireCommand(
                request.FireSequence,
                request.RequestTick,
                request.AimX,
                request.AimZ,
                request.EstimatedRttSeconds,
                request.InterpolationDelaySeconds);

            FireReceiptDecision decision = world.TryQueueFireWithReceipt(client.PlayerId, command);
            receipt = new FireReceiptMessage
            {
                Type = "FireReceipt",
                PlayerId = client.PlayerId,
                FireSequence = decision.FireSequence,
                Accepted = decision.Accepted,
                Reason = decision.Reason,
                ServerTick = decision.ServerTick
            };

            if (options.LogCommandDecisions || !decision.Accepted)
            {
                Console.WriteLine(
                    $"FireRequest {(decision.Accepted ? "queued" : "rejected")}: " +
                    $"playerId={client.PlayerId}, sequence={command.FireSequence}, " +
                    $"requestTick={command.RequestTick}, reason={decision.Reason}, duplicate={decision.IsDuplicate}.");
            }
        }

        if (receipt != null)
        {
            await SendJsonAsync(receipt, remoteEndPoint);
        }
    }

    private async Task SendSnapshotAsync(SnapshotSendWork work)
    {
        string json = JsonSerializer.Serialize(work.Plan.Snapshot);
        byte[] bytes = Encoding.UTF8.GetBytes(json);
        await udpServer.SendAsync(bytes, bytes.Length, work.Client.RemoteEndPoint);
        work.Client.Replicator.RecordSentSnapshot(bytes.Length, work.Plan);
        sentSnapshotCount++;
    }

    private void HandleClientGoodbye(IPEndPoint remoteEndPoint)
    {
        lock (stateLock)
        {
            if (!clientRegistry.TryRemove(remoteEndPoint, out ConnectedClient? client) || client == null)
            {
                Console.WriteLine("ClientGoodbye ignored: sender is not registered.");
                return;
            }

            world.RemovePlayer(client.PlayerId);
            Console.WriteLine($"Client disconnected: playerId={client.PlayerId}, name={client.Name}.");
        }
    }

    private void HandleEventAck(IPEndPoint remoteEndPoint, string json)
    {
        EventAckMessage? acknowledgement;

        try
        {
            acknowledgement = JsonSerializer.Deserialize<EventAckMessage>(json);
        }
        catch (JsonException exception)
        {
            Console.WriteLine($"EventAck ignored: invalid JSON. {exception.Message}");
            return;
        }

        if (acknowledgement == null || acknowledgement.EventId <= 0)
        {
            Console.WriteLine("EventAck ignored: eventId must be positive.");
            return;
        }

        lock (stateLock)
        {
            if (!clientRegistry.TryGetClient(remoteEndPoint, out ConnectedClient? client) || client == null)
            {
                Console.WriteLine("EventAck ignored: sender has not completed ClientHello.");
                return;
            }

            if (!client.ReliableEvents.Acknowledge(acknowledgement.EventId, DateTime.UtcNow))
            {
                Console.WriteLine($"EventAck ignored: client={client.PlayerId}, eventId={acknowledgement.EventId} is not pending.");
            }
        }
    }

    private void LogServerTelemetry()
    {
        Console.WriteLine(
            $"Tick={world.ServerTick}, players={world.Players.Count}, " +
            $"input={world.AcceptedInputCount}/{world.ReceivedInputCount}, inputRejected={world.RejectedInputCount}, " +
            $"inputSuperseded={world.SupersededInputCount}, inputReasons=[{world.GetInputRejectionSummary()}], " +
            $"snapshotDatagrams={sentSnapshotCount}, snapshotRate={GetSnapshotRate()}Hz, " +
            $"distanceFiltering={options.ReplicationSettings.EnableDistanceFiltering}, " +
            $"fire={world.AcceptedFireRequestCount}/{world.ReceivedFireRequestCount}, " +
            $"fireRejected={world.RejectedFireRequestCount}, fireReasons=[{world.GetFireRejectionSummary()}], " +
            $"events={sentGameplayEventCount}, reliableEventDatagrams={sentReliableEventDatagramCount}, lagCompFire={world.LagCompensatedFireRequestCount}, " +
            $"deaths={world.DeathCount}, respawns={world.RespawnCount}");

        foreach (ConnectedClient client in clientRegistry.GetAllClients().OrderBy(client => client.PlayerId))
        {
            ClientReplicationTelemetry telemetry = client.Replicator.ConsumeTelemetry();
            ReliableEventLedgerTelemetry reliableTelemetry = client.ReliableEvents.GetTelemetry();
            Console.WriteLine(
                $"Replication client={client.PlayerId}, snapshots={telemetry.SnapshotCount}/s, " +
                $"statePlayers={telemetry.LastPlayerCount}, scopePlayers={telemetry.LastScopedPlayerCount}, " +
                $"bytes={telemetry.ByteCount}/s, reliablePending={reliableTelemetry.PendingCount}, " +
                $"reliableResends={reliableTelemetry.ResendCount}, acked={reliableTelemetry.AcknowledgedCount}, " +
                $"avgAck={reliableTelemetry.AverageAcknowledgementLatencyMilliseconds:F0}ms, " +
                $"retryLimit={reliableTelemetry.RetryLimitExceededCount}.");
        }
    }

    private int GetSnapshotRate()
    {
        return Math.Clamp(options.ReplicationSettings.SnapshotRate, 1, Math.Max(1, options.TickRate));
    }

    private async Task SendRawJsonAsync(string json, IPEndPoint target)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(json);
        await udpServer.SendAsync(bytes, bytes.Length, target);
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
                    FireSequence = fireEvent.FireSequence,
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

            case FireResultWorldEvent fireResult:
                return new FireResultMessage
                {
                    Type = "FireResult",
                    ServerTick = fireResult.ServerTick,
                    PlayerId = fireResult.ShooterPlayerId,
                    FireSequence = fireResult.FireSequence,
                    Result = fireResult.Result,
                    TargetPlayerId = fireResult.TargetPlayerId
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

            case DeathWorldEvent deathEvent:
                return new DeathEventMessage
                {
                    Type = "DeathEvent",
                    ServerTick = deathEvent.ServerTick,
                    PlayerId = deathEvent.PlayerId,
                    LifeStateVersion = deathEvent.LifeStateVersion,
                    KillerPlayerId = deathEvent.KillerPlayerId,
                    RespawnRemainingSeconds = deathEvent.RespawnRemainingSeconds
                };

            case RespawnWorldEvent respawnEvent:
                return new RespawnEventMessage
                {
                    Type = "RespawnEvent",
                    ServerTick = respawnEvent.ServerTick,
                    PlayerId = respawnEvent.PlayerId,
                    LifeStateVersion = respawnEvent.LifeStateVersion,
                    X = respawnEvent.X,
                    Y = respawnEvent.Y,
                    Z = respawnEvent.Z,
                    Health = respawnEvent.Health,
                    MaxHealth = respawnEvent.MaxHealth
                };

            case KillWorldEvent killEvent:
                return new KillEventMessage
                {
                    Type = "KillEvent",
                    ServerTick = killEvent.ServerTick,
                    KillerPlayerId = killEvent.KillerPlayerId,
                    VictimPlayerId = killEvent.VictimPlayerId
                };

            case MatchEndWorldEvent matchEndEvent:
                return new MatchEndEventMessage
                {
                    Type = "MatchEndEvent",
                    ServerTick = matchEndEvent.ServerTick,
                    WinnerPlayerId = matchEndEvent.WinnerPlayerId
                };

            default:
                throw new InvalidOperationException($"Unsupported world event '{worldEvent.GetType().Name}'.");
        }
    }

    private static bool IsReliableWorldEvent(GameWorldEvent worldEvent)
    {
        return worldEvent is DeathWorldEvent
            || worldEvent is RespawnWorldEvent
            || worldEvent is KillWorldEvent
            || worldEvent is MatchEndWorldEvent
            || worldEvent is FireResultWorldEvent;
    }

    private static int[] GetRelatedPlayerIds(GameWorldEvent worldEvent)
    {
        return worldEvent switch
        {
            FireResolvedEvent fireEvent => new[] { fireEvent.ShooterPlayerId },
            FireResultWorldEvent fireResult => new[] { fireResult.ShooterPlayerId },
            HitResolvedEvent hitEvent => new[] { hitEvent.ShooterPlayerId, hitEvent.TargetPlayerId },
            HealthChangedWorldEvent healthEvent => new[] { healthEvent.PlayerId },
            // Death and respawn drive an Avatar's local visual state, so only the affected
            // entity being in scope makes them eligible for delivery.
            DeathWorldEvent deathEvent => new[] { deathEvent.PlayerId },
            RespawnWorldEvent respawnEvent => new[] { respawnEvent.PlayerId },
            KillWorldEvent killEvent => new[] { killEvent.KillerPlayerId, killEvent.VictimPlayerId },
            MatchEndWorldEvent => Array.Empty<int>(),
            _ => Array.Empty<int>()
        };
    }

    private static bool IsEventRelevantToClient(ConnectedClient client, int[] relatedPlayerIds, IReadOnlyList<ReplicationCandidate> candidates)
    {
        return relatedPlayerIds.Length == 0
            || relatedPlayerIds.Any(playerId => client.Replicator.IsPlayerInReplicationScope(client.PlayerId, playerId, candidates));
    }

    private static void SetReliableEventId(object networkEvent, long eventId)
    {
        switch (networkEvent)
        {
            case DeathEventMessage deathEvent:
                deathEvent.EventId = eventId;
                break;
            case RespawnEventMessage respawnEvent:
                respawnEvent.EventId = eventId;
                break;
            case KillEventMessage killEvent:
                killEvent.EventId = eventId;
                break;
            case MatchEndEventMessage matchEndEvent:
                matchEndEvent.EventId = eventId;
                break;
            case FireResultMessage fireResult:
                fireResult.EventId = eventId;
                break;
            default:
                throw new InvalidOperationException($"Event '{networkEvent.GetType().Name}' is not a reliable event.");
        }
    }

    private readonly struct SnapshotSendWork
    {
        public SnapshotSendWork(ConnectedClient client, ClientSnapshotPlan plan)
        {
            Client = client;
            Plan = plan;
        }

        public ConnectedClient Client { get; }
        public ClientSnapshotPlan Plan { get; }
    }

    private readonly struct UnreliableEventSendWork
    {
        public UnreliableEventSendWork(IPEndPoint target, string json)
        {
            Target = target;
            Json = json;
        }

        public IPEndPoint Target { get; }
        public string Json { get; }
    }

    private readonly struct ReliableEventSendWork
    {
        public ReliableEventSendWork(IPEndPoint target, string json)
        {
            Target = target;
            Json = json;
        }

        public IPEndPoint Target { get; }
        public string Json { get; }
    }
}
