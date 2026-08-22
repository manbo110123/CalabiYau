using CalabiYau.CollisionCore;
using CalabiYau.TankCollision;

public sealed class GameWorldSettings
{
    public int ServerTickRate { get; set; } = 30;
    public float PlayerMoveSpeed { get; set; } = 7f;
    public float PlayerTurnDegreesPerSecond { get; set; } = 180f;
    public int MaxHealth { get; set; } = 100;
    public int FireDamage { get; set; } = 25;
    public float FireCooldownSeconds { get; set; } = 0.75f;
    public float RespawnDelaySeconds { get; set; } = 3f;
    public float FireRange { get; set; } = 35f;
    public float HitRadius { get; set; } = 1.2f;
    public float AimToleranceMeters { get; set; } = 3f;
    public float MuzzleForwardOffsetMeters { get; set; } = 1.4f;
    public float MuzzleHeightMeters { get; set; } = 1.2f;
    public bool EnableLagCompensation { get; set; } = true;
    public float LagCompensationHistorySeconds { get; set; } = 1f;
    public float LagCompensationMaxRewindSeconds { get; set; } = 0.35f;

    // At 30 Hz this keeps about two seconds of commands. It absorbs a short burst of UDP
    // reordering without making the server retain an unbounded client-controlled queue.
    public int InputBufferCapacity { get; set; } = 64;

    // A latest-input simulation needs an expiry. Without this lease, a lost key-up packet
    // could make a player keep moving forever on the authoritative server.
    public int InputHoldTimeoutTicks { get; set; } = 6;
    public int FireBufferCapacity { get; set; } = 16;

    // The client only retries a FireRequest for a short, fixed period. Keep enough receipt
    // decisions to answer those retries without making player-owned history unbounded.
    public int FireReceiptHistoryCapacity { get; set; } = 128;

    // A client may be up to one second ahead of the latest consumed input. Larger windows
    // tolerate jitter, but also retain more stale intent when the client has fallen behind.
    public int MaxInputTickAhead { get; set; } = 30;
    public int MaxInputTickLag { get; set; } = 120;

    // PlayerInput contains a ground-plane aim point rather than a normalized vector.
    public float MaxInputAimDistanceMeters { get; set; } = 100f;
    public float MaxReportedRttSeconds { get; set; } = 2f;
    public float MaxReportedInterpolationDelaySeconds { get; set; } = 0.5f;
}

// Owns all authoritative game rules and can run without UDP, JSON, or Unity.
public sealed class GameWorld
{
    private readonly GameWorldSettings settings;
    private readonly TankWorldCollision2D collisionWorld;
    private readonly TankMapQueries2D mapQueries;
    private readonly Dictionary<int, PlayerState> playersById = new Dictionary<int, PlayerState>();
    private readonly Dictionary<string, long> inputRejectionsByReason = new Dictionary<string, long>();
    private readonly Dictionary<string, long> fireRejectionsByReason = new Dictionary<string, long>();
    private readonly Dictionary<string, long> spawnRejectionsByReason = new Dictionary<string, long>();

    public GameWorld()
        : this(new GameWorldSettings(), null)
    {
    }

    public GameWorld(
        GameWorldSettings settings,
        TankWorldCollision2D? collisionWorld = null)
    {
        this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        this.collisionWorld = collisionWorld ?? TrainingCollisionMap2D.CreateResolver();
        mapQueries = new TankMapQueries2D(this.collisionWorld.Map);
    }

    public int ServerTick { get; private set; }
    public int MaxHealth => settings.MaxHealth;
    public IReadOnlyCollection<PlayerState> Players => playersById.Values;
    public long ReceivedInputCount { get; private set; }
    public long AcceptedInputCount { get; private set; }
    public long RejectedInputCount { get; private set; }
    public long SupersededInputCount { get; private set; }
    public long ReceivedFireRequestCount { get; private set; }
    public long QueuedFireRequestCount { get; private set; }
    public long AcceptedFireRequestCount { get; private set; }
    public long RejectedFireRequestCount { get; private set; }
    public long DeathCount { get; private set; }
    public long RespawnCount { get; private set; }
    public long LagCompensatedFireRequestCount { get; private set; }
    public long BlockedMovementTickCount { get; private set; }
    public long BlockedRotationTickCount { get; private set; }
    public long CollisionResolutionCount { get; private set; }
    public long StaticOccludedFireCount { get; private set; }
    public long SpawnCandidateRejectionCount { get; private set; }
    public long SpawnPlacementFailureCount { get; private set; }
    public IReadOnlyDictionary<string, long> InputRejectionsByReason => inputRejectionsByReason;
    public IReadOnlyDictionary<string, long> FireRejectionsByReason => fireRejectionsByReason;
    public IReadOnlyDictionary<string, long> SpawnRejectionsByReason => spawnRejectionsByReason;

    public bool AddPlayer(int playerId)
    {
        return AddPlayer(playerId, out _);
    }

    public bool AddPlayer(int playerId, out string rejectionReason)
    {
        if (playersById.ContainsKey(playerId))
        {
            rejectionReason = "duplicate-player";
            return false;
        }

        if (!TryFindSpawnRootPosition(playerId, out Vec2D spawn, out rejectionReason))
        {
            SpawnPlacementFailureCount++;
            return false;
        }

        playersById.Add(playerId, CreateInitialPlayerState(playerId, spawn));
        rejectionReason = string.Empty;
        return true;
    }

    // The transport owns connection lifetime; removing a player here guarantees that a
    // disconnected endpoint cannot remain in authoritative snapshots or hit detection.
    public bool RemovePlayer(int playerId)
    {
        return playersById.Remove(playerId);
    }

    // This is the authoritative input gate. Transport code only creates the command and
    // identifies its sender; all sequencing and gameplay validation happens here.
    public CommandGateResult TryQueueInput(int playerId, InputCommand input)
    {
        ReceivedInputCount++;

        if (!playersById.TryGetValue(playerId, out PlayerState? player))
        {
            return RejectInput("unknown-player");
        }

        if (!player.IsAlive)
        {
            return RejectInput("player-dead");
        }

        if (input.InputTick <= 0)
        {
            return RejectInput("invalid-input-tick");
        }

        if (!IsFinite(input.MoveAxis)
            || !IsFinite(input.TurnAxis)
            || !IsFinite(input.AimX)
            || !IsFinite(input.AimZ))
        {
            return RejectInput("non-finite-input-value");
        }

        if (input.MoveAxis < -1f || input.MoveAxis > 1f
            || input.TurnAxis < -1f || input.TurnAxis > 1f)
        {
            return RejectInput("movement-axis-out-of-range");
        }

        float aimDistanceX = input.AimX - player.X;
        float aimDistanceZ = input.AimZ - player.Z;
        float aimDistance = MathF.Sqrt(aimDistanceX * aimDistanceX + aimDistanceZ * aimDistanceZ);

        if (aimDistance > settings.MaxInputAimDistanceMeters)
        {
            return RejectInput("aim-point-too-far");
        }

        if (input.InputTick < player.LastProcessedInputTick - settings.MaxInputTickLag)
        {
            return RejectInput("input-too-old");
        }

        if (input.InputTick <= player.LastProcessedInputTick)
        {
            return RejectInput("duplicate-or-already-processed-input");
        }

        if (input.InputTick > player.LastProcessedInputTick + settings.MaxInputTickAhead)
        {
            return RejectInput("input-too-far-in-future");
        }

        if (player.PendingInputs.ContainsKey(input.InputTick))
        {
            return RejectInput("duplicate-buffered-input");
        }

        if (player.PendingInputs.Count >= settings.InputBufferCapacity)
        {
            return RejectInput("input-buffer-full");
        }

        player.PendingInputs.Add(input.InputTick, input);
        AcceptedInputCount++;
        return CommandGateResult.Accepted("queued");
    }

    public string GetInputRejectionSummary()
    {
        return FormatRejectionSummary(inputRejectionsByReason);
    }

    public string GetFireRejectionSummary()
    {
        return FormatRejectionSummary(fireRejectionsByReason);
    }

    public string GetSpawnRejectionSummary()
    {
        return FormatRejectionSummary(spawnRejectionsByReason);
    }

    // Fire is a discrete command. Reception only validates and queues it; the fixed Tick
    // resolves cooldown, hit detection, damage, and events together with all other rules.
    public CommandGateResult TryQueueFire(int playerId, FireCommand command)
    {
        FireReceiptDecision decision = TryQueueFireWithReceipt(playerId, command);

        // Keep the legacy command-gate API useful to offline callers: a retry did not
        // queue a new command. The transport uses TryQueueFireWithReceipt so it can send
        // the original accepted/rejected receipt back to the UDP client.
        return decision.IsDuplicate
            ? CommandGateResult.Rejected("duplicate-or-out-of-order-fire")
            : decision.ToCommandGateResult();
    }

    // A FireReceiptDecision represents only the server's receive/queue decision. It is
    // remembered per sequence so a UDP retry cannot create a second queued command or
    // change the response after world state has advanced.
    public FireReceiptDecision TryQueueFireWithReceipt(int playerId, FireCommand command)
    {
        ReceivedFireRequestCount++;

        if (!playersById.TryGetValue(playerId, out PlayerState? shooter))
        {
            return CreateFireReceipt(command.FireSequence, RejectFire("unknown-player"));
        }

        if (shooter.FireReceipts.TryGetValue(command.FireSequence, out FireReceiptDecision cachedReceipt))
        {
            return cachedReceipt.AsDuplicate();
        }

        CommandGateResult result;

        if (!shooter.IsAlive)
        {
            result = RejectFire("player-dead");
            return RememberFireReceipt(shooter, command.FireSequence, result);
        }

        if (!IsValidFireCommand(command, shooter, out string rejectReason))
        {
            result = RejectFire("invalid-fire-command");
            return RememberFireReceipt(shooter, command.FireSequence, result);
        }

        if (command.RequestTick < shooter.LastProcessedInputTick - settings.MaxInputTickLag)
        {
            result = RejectFire("request-too-old");
            return RememberFireReceipt(shooter, command.FireSequence, result);
        }

        if (command.RequestTick > shooter.LastProcessedInputTick + settings.MaxInputTickAhead)
        {
            result = RejectFire("request-too-far-in-future");
            return RememberFireReceipt(shooter, command.FireSequence, result);
        }

        if (command.FireSequence <= shooter.LastQueuedFireSequence)
        {
            result = RejectFire("duplicate-or-out-of-order-fire");
            return RememberFireReceipt(shooter, command.FireSequence, result);
        }

        if (shooter.PendingFires.Count >= settings.FireBufferCapacity)
        {
            result = RejectFire("fire-buffer-full");
            return RememberFireReceipt(shooter, command.FireSequence, result);
        }

        shooter.PendingFires.Add(command.FireSequence, command);
        shooter.LastQueuedFireSequence = command.FireSequence;
        QueuedFireRequestCount++;
        return RememberFireReceipt(shooter, command.FireSequence, CommandGateResult.Accepted("queued"));
    }

    public List<GameWorldEvent> Tick(float deltaTime)
    {
        ServerTick++;
        List<GameWorldEvent> eventsToBroadcast = new List<GameWorldEvent>();

        foreach (PlayerState player in playersById.Values)
        {
            List<GameWorldEvent>? respawnEvents = TryRespawnPlayer(player);

            if (respawnEvents != null)
            {
                eventsToBroadcast.AddRange(respawnEvents);
            }

            ConsumeLatestInputForTick(player);
            SimulatePlayer(player, deltaTime);
            StorePlayerHistory(player);
        }

        foreach (PlayerState player in playersById.Values)
        {
            ResolveQueuedFireRequests(player, eventsToBroadcast);
        }

        return eventsToBroadcast;
    }

    private void ResolveQueuedFireRequests(PlayerState shooter, List<GameWorldEvent> eventsToBroadcast)
    {
        if (shooter.PendingFires.Count == 0)
        {
            return;
        }

        foreach (FireCommand command in shooter.PendingFires.Values)
        {
            shooter.LastProcessedFireSequence = command.FireSequence;

            if (!shooter.IsAlive)
            {
                RejectFire("player-dead");
                AddFireResult(eventsToBroadcast, shooter.PlayerId, command.FireSequence, "rejected-player-dead");
                continue;
            }

            if (ServerTick < shooter.NextAllowedFireServerTick)
            {
                RejectFire("cooldown");
                AddFireResult(eventsToBroadcast, shooter.PlayerId, command.FireSequence, "rejected-cooldown");
                continue;
            }

            LagCompensationFrame shooterFrame = BuildLagCompensationFrame(shooter, command);

            if (!IsRequestedAimReasonable(shooter.AimX, shooter.AimZ, command)
                && !IsRequestedAimReasonable(shooterFrame.AimX, shooterFrame.AimZ, command))
            {
                RejectFire("aim-mismatch");
                AddFireResult(eventsToBroadcast, shooter.PlayerId, command.FireSequence, "rejected-aim-mismatch");
                continue;
            }

            AcceptedFireRequestCount++;
            shooter.LastCombatServerTick = ServerTick;
            shooter.NextAllowedFireServerTick = ServerTick + Math.Max(1, (int)MathF.Ceiling(settings.FireCooldownSeconds * settings.ServerTickRate));

            if (shooterFrame.RewindTicks > 0)
            {
                LagCompensatedFireRequestCount++;
            }

            FireRay fireRay = BuildFireRay(shooterFrame.X, shooterFrame.Z, shooterFrame.BodyYaw, command);
            Ray2D staticRay = new Ray2D(
                new Vec2D(fireRay.OriginX, fireRay.OriginZ),
                new Vec2D(fireRay.DirectionX, fireRay.DirectionZ));
            bool hasStaticHit = mapQueries.RaycastStatic(
                staticRay,
                settings.FireRange,
                out StaticRaycastHit2D staticHit);
            int candidatePlayerId = FindFirstHitPlayer(
                shooter.PlayerId,
                fireRay,
                shooterFrame.HitTestServerTick,
                out float candidatePlayerDistance);
            bool staticObstacleWins = hasStaticHit
                && (candidatePlayerId == 0
                    || staticHit.Distance <= candidatePlayerDistance + collisionWorld.Map.Epsilon);
            int hitPlayerId = staticObstacleWins ? 0 : candidatePlayerId;
            float hitDistance = hitPlayerId == 0 ? 0f : candidatePlayerDistance;

            if (staticObstacleWins)
            {
                StaticOccludedFireCount++;
            }

            float presentationRange = hasStaticHit
                ? MathF.Min(settings.FireRange, staticHit.Distance)
                : settings.FireRange;

            AddFireResult(
                eventsToBroadcast,
                shooter.PlayerId,
                command.FireSequence,
                hitPlayerId != 0 ? "fired-hit" : "fired-no-hit",
                hitPlayerId);

            eventsToBroadcast.Add(new FireResolvedEvent
            {
                ServerTick = ServerTick,
                ShooterPlayerId = shooter.PlayerId,
                FireSequence = command.FireSequence,
                RequestTick = command.RequestTick,
                OriginX = fireRay.OriginX,
                OriginY = fireRay.OriginY,
                OriginZ = fireRay.OriginZ,
                DirectionX = fireRay.DirectionX,
                DirectionY = 0f,
                DirectionZ = fireRay.DirectionZ,
                Range = presentationRange,
                LagCompensated = shooterFrame.RewindTicks > 0,
                HitTestServerTick = shooterFrame.HitTestServerTick,
                RewindSeconds = shooterFrame.RewindSeconds
            });

            if (hitPlayerId != 0 && playersById.TryGetValue(hitPlayerId, out PlayerState? hitPlayer))
            {
                hitPlayer.LastCombatServerTick = ServerTick;
                int oldHealth = hitPlayer.Health;
                hitPlayer.Health = Math.Max(0, hitPlayer.Health - settings.FireDamage);

                if (hitPlayer.IsAlive && hitPlayer.Health <= 0)
                {
                    KillPlayer(hitPlayer);
                }

                float hitX = fireRay.OriginX + fireRay.DirectionX * hitDistance;
                float hitZ = fireRay.OriginZ + fireRay.DirectionZ * hitDistance;

                eventsToBroadcast.Add(new HitResolvedEvent
                {
                    ServerTick = ServerTick,
                    ShooterPlayerId = shooter.PlayerId,
                    TargetPlayerId = hitPlayer.PlayerId,
                    HitX = hitX,
                    HitY = fireRay.OriginY,
                    HitZ = hitZ,
                    Damage = oldHealth - hitPlayer.Health
                });

                eventsToBroadcast.Add(CreateHealthChangedEvent(hitPlayer));

                if (hitPlayer.Health <= 0)
                {
                    eventsToBroadcast.Add(new DeathWorldEvent
                    {
                        ServerTick = ServerTick,
                        PlayerId = hitPlayer.PlayerId,
                        LifeStateVersion = hitPlayer.LifeStateVersion,
                        KillerPlayerId = shooter.PlayerId,
                        RespawnRemainingSeconds = GetRespawnRemainingSeconds(hitPlayer)
                    });
                    eventsToBroadcast.Add(new KillWorldEvent
                    {
                        ServerTick = ServerTick,
                        KillerPlayerId = shooter.PlayerId,
                        VictimPlayerId = hitPlayer.PlayerId
                    });
                }
            }
        }

        shooter.PendingFires.Clear();
    }

    // Every command which received an accepted FireReceipt reaches this point exactly once.
    // The result is deliberately separate from FireResolvedEvent/HitResolvedEvent: those
    // remain ordinary presentation events, while this is the shooter's reliable final answer.
    private void AddFireResult(
        List<GameWorldEvent> eventsToBroadcast,
        int shooterPlayerId,
        int fireSequence,
        string result,
        int targetPlayerId = 0)
    {
        eventsToBroadcast.Add(new FireResultWorldEvent
        {
            ServerTick = ServerTick,
            ShooterPlayerId = shooterPlayerId,
            FireSequence = fireSequence,
            Result = result,
            TargetPlayerId = targetPlayerId
        });
    }

    public float GetRespawnRemainingSeconds(PlayerState player)
    {
        if (player.IsAlive || player.RespawnServerTick <= 0)
        {
            return 0f;
        }

        int remainingTicks = Math.Max(0, player.RespawnServerTick - ServerTick);
        return remainingTicks / (float)settings.ServerTickRate;
    }

    private PlayerState CreateInitialPlayerState(int playerId, Vec2D spawn)
    {
        PlayerState player = new PlayerState
        {
            PlayerId = playerId,
            X = spawn.X,
            Y = 0f,
            Z = spawn.Y,
            BodyYaw = 0f,
            AimX = spawn.X,
            AimZ = spawn.Y + 5f,
            Health = settings.MaxHealth,
            IsAlive = true,
            LifeStateVersion = 1,
            NextAllowedFireServerTick = 0,
            RespawnServerTick = 0,
            LatestInput = new InputCommand(0, 0f, 0f, spawn.X, spawn.Y + 5f)
        };

        StorePlayerHistory(player);
        return player;
    }

    private bool TryFindSpawnRootPosition(
        int playerId,
        out Vec2D spawn,
        out string rejectionReason)
    {
        for (int candidateOffset = 0;
             candidateOffset < TrainingCollisionMap2D.SpawnCandidateCount;
             candidateOffset++)
        {
            Vec2D candidate = TrainingCollisionMap2D.GetSpawnCandidateRootPosition(
                playerId,
                candidateOffset);
            TankPose2D candidatePose = new TankPose2D(candidate, 0f);
            Obb2D candidateShape = collisionWorld.CreateTankShape(candidatePose);

            if (!mapQueries.IsInsideWorldBounds(candidateShape))
            {
                RejectSpawnCandidate("outside-world-bounds");
                continue;
            }

            if (mapQueries.OverlapStatic(candidateShape, out StaticOverlapHit2D staticHit)
                && staticHit.PenetrationDepth > collisionWorld.Map.Epsilon)
            {
                RejectSpawnCandidate("overlaps-static-collider");
                continue;
            }

            bool occupiedByAlivePlayer = false;

            foreach (PlayerState otherPlayer in playersById.Values)
            {
                if (otherPlayer.PlayerId == playerId || !otherPlayer.IsAlive)
                {
                    continue;
                }

                Obb2D otherShape = collisionWorld.CreateTankShape(new TankPose2D(
                    new Vec2D(otherPlayer.X, otherPlayer.Z),
                    DegreesToRadians(otherPlayer.BodyYaw)));

                OverlapResult2D occupancy = CollisionQueries2D.Overlap(
                        candidateShape,
                        otherShape,
                        collisionWorld.Map.Epsilon);

                if (occupancy.Hit
                    && occupancy.PenetrationDepth > collisionWorld.Map.Epsilon)
                {
                    occupiedByAlivePlayer = true;
                    break;
                }
            }

            if (occupiedByAlivePlayer)
            {
                RejectSpawnCandidate("occupied-by-alive-player");
                continue;
            }

            spawn = candidate;
            rejectionReason = string.Empty;
            return true;
        }

        spawn = Vec2D.Zero;
        rejectionReason = "no-valid-spawn-candidate";
        return false;
    }

    private void RejectSpawnCandidate(string reason)
    {
        SpawnCandidateRejectionCount++;
        IncrementRejectionCount(spawnRejectionsByReason, reason);
    }

    private void SimulatePlayer(PlayerState player, float deltaTime)
    {
        if (!player.IsAlive)
        {
            return;
        }

        InputCommand input = player.LatestInput;
        TankPose2D start = new TankPose2D(
            new Vec2D(player.X, player.Z),
            DegreesToRadians(player.BodyYaw));
        TankMoveResult2D movement = TankCommandSimulation2D.Simulate(
            collisionWorld,
            start,
            input.MoveAxis,
            input.TurnAxis,
            settings.PlayerMoveSpeed,
            settings.PlayerTurnDegreesPerSecond,
            deltaTime);

        player.X = movement.Pose.Position.X;
        player.Z = movement.Pose.Position.Y;
        player.BodyYaw = RadiansToDegrees(movement.Pose.GameplayYawRadians);
        player.AimX = input.AimX;
        player.AimZ = input.AimZ;

        if (movement.WasBlocked)
        {
            BlockedMovementTickCount++;
        }

        if (movement.RotationBlocked)
        {
            BlockedRotationTickCount++;
        }

        CollisionResolutionCount += movement.CollisionCount;
    }

    private void KillPlayer(PlayerState player)
    {
        player.IsAlive = false;
        player.Health = 0;
        player.LifeStateVersion++;
        player.RespawnServerTick = ServerTick + Math.Max(1, (int)MathF.Ceiling(settings.RespawnDelaySeconds * settings.ServerTickRate));
        player.NextAllowedFireServerTick = player.RespawnServerTick;
        player.LatestInput = new InputCommand(
            player.LatestInput.InputTick,
            0f,
            0f,
            player.LatestInput.AimX,
            player.LatestInput.AimZ);
        player.PendingInputs.Clear();
        // Keep already accepted FireRequests until this Tick reaches their resolver. They
        // will become rejected-player-dead FireResults rather than silently disappearing.
        DeathCount++;
    }

    private List<GameWorldEvent>? TryRespawnPlayer(PlayerState player)
    {
        if (player.IsAlive || player.RespawnServerTick <= 0 || ServerTick < player.RespawnServerTick)
        {
            return null;
        }

        if (!TryFindSpawnRootPosition(player.PlayerId, out Vec2D spawn, out _))
        {
            // Keep the player dead and retry at a bounded cadence. This makes a full
            // spawn set observable without silently inserting a Tank into a wall/player.
            SpawnPlacementFailureCount++;
            player.RespawnServerTick = ServerTick + Math.Max(1, settings.ServerTickRate);
            return null;
        }

        player.X = spawn.X;
        player.Y = 0f;
        player.Z = spawn.Y;
        player.BodyYaw = 0f;
        player.AimX = spawn.X;
        player.AimZ = spawn.Y + 5f;
        player.Health = settings.MaxHealth;
        player.IsAlive = true;
        player.LifeStateVersion++;
        player.RespawnServerTick = 0;
        player.NextAllowedFireServerTick = ServerTick + Math.Max(1, (int)MathF.Ceiling(0.25f * settings.ServerTickRate));
        player.LatestInput = new InputCommand(
            player.LatestInput.InputTick,
            0f,
            0f,
            spawn.X,
            spawn.Y + 5f);
        player.PendingInputs.Clear();
        player.PendingFires.Clear();
        RespawnCount++;

        return new List<GameWorldEvent>
        {
            CreateHealthChangedEvent(player),
            new RespawnWorldEvent
            {
                ServerTick = ServerTick,
                PlayerId = player.PlayerId,
                LifeStateVersion = player.LifeStateVersion,
                X = player.X,
                Y = player.Y,
                Z = player.Z,
                Health = player.Health,
                MaxHealth = settings.MaxHealth
            }
        };
    }

    // The buffer is sorted for deterministic inspection, but each server tick uses the
    // newest command currently available. This preserves the old responsive "latest input"
    // behavior while ensuring a late packet can never move the world backward in time.
    private void ConsumeLatestInputForTick(PlayerState player)
    {
        if (player.PendingInputs.Count == 0)
        {
            if (ServerTick - player.LastConsumedInputServerTick > settings.InputHoldTimeoutTicks)
            {
                player.LatestInput = new InputCommand(
                    player.LatestInput.InputTick,
                    0f,
                    0f,
                    player.LatestInput.AimX,
                    player.LatestInput.AimZ);
            }

            return;
        }

        KeyValuePair<int, InputCommand> newestInput = player.PendingInputs.Last();
        int discardedInputCount = player.PendingInputs.Count - 1;
        player.PendingInputs.Clear();
        player.LatestInput = newestInput.Value;
        player.LastProcessedInputTick = newestInput.Key;
        player.LastConsumedInputServerTick = ServerTick;
        SupersededInputCount += discardedInputCount;
    }

    private HealthChangedWorldEvent CreateHealthChangedEvent(PlayerState player)
    {
        return new HealthChangedWorldEvent
        {
            ServerTick = ServerTick,
            PlayerId = player.PlayerId,
            Health = player.Health,
            MaxHealth = settings.MaxHealth,
            IsAlive = player.IsAlive,
            RespawnRemainingSeconds = GetRespawnRemainingSeconds(player)
        };
    }

    private void StorePlayerHistory(PlayerState player)
    {
        player.History.Add(new PlayerHistoryFrame
        {
            ServerTick = ServerTick,
            X = player.X,
            Y = player.Y,
            Z = player.Z,
            BodyYaw = player.BodyYaw,
            AimX = player.AimX,
            AimZ = player.AimZ,
            IsAlive = player.IsAlive
        });

        int oldestAllowedTick = ServerTick - Math.Max(1, (int)MathF.Ceiling(settings.LagCompensationHistorySeconds * settings.ServerTickRate));

        while (player.History.Count > 0 && player.History[0].ServerTick < oldestAllowedTick)
        {
            player.History.RemoveAt(0);
        }
    }

    private LagCompensationFrame BuildLagCompensationFrame(PlayerState shooter, FireCommand command)
    {
        float requestedRewindSeconds = settings.EnableLagCompensation
            ? MathF.Max(0f, command.EstimatedRttSeconds + command.InterpolationDelaySeconds)
            : 0f;
        float rewindSeconds = MathF.Min(requestedRewindSeconds, settings.LagCompensationMaxRewindSeconds);
        int rewindTicks = Math.Max(0, (int)MathF.Round(rewindSeconds * settings.ServerTickRate));
        int hitTestServerTick = Math.Max(0, ServerTick - rewindTicks);
        PlayerHistoryFrame shooterHistory = GetPlayerFrameAtOrBefore(shooter, hitTestServerTick);

        return new LagCompensationFrame
        {
            HitTestServerTick = hitTestServerTick,
            RewindTicks = rewindTicks,
            RewindSeconds = rewindTicks / (float)settings.ServerTickRate,
            X = shooterHistory.X,
            Y = shooterHistory.Y,
            Z = shooterHistory.Z,
            BodyYaw = shooterHistory.BodyYaw,
            AimX = shooterHistory.AimX,
            AimZ = shooterHistory.AimZ
        };
    }

    private PlayerHistoryFrame GetPlayerFrameAtOrBefore(PlayerState player, int targetServerTick)
    {
        for (int index = player.History.Count - 1; index >= 0; index--)
        {
            if (player.History[index].ServerTick <= targetServerTick)
            {
                return player.History[index];
            }
        }

        if (player.History.Count > 0)
        {
            return player.History[0];
        }

        return new PlayerHistoryFrame
        {
            ServerTick = ServerTick,
            X = player.X,
            Y = player.Y,
            Z = player.Z,
            BodyYaw = player.BodyYaw,
            AimX = player.AimX,
            AimZ = player.AimZ,
            IsAlive = player.IsAlive
        };
    }

    private bool IsValidFireCommand(FireCommand command, PlayerState shooter, out string rejectReason)
    {
        if (command.FireSequence <= 0)
        {
            rejectReason = "fireSequence must be positive";
            return false;
        }

        if (command.RequestTick <= 0)
        {
            rejectReason = "requestTick must be positive";
            return false;
        }

        if (!IsFinite(command.AimX)
            || !IsFinite(command.AimZ)
            || !IsFinite(command.EstimatedRttSeconds)
            || !IsFinite(command.InterpolationDelaySeconds))
        {
            rejectReason = "FireRequest contains a non-finite value";
            return false;
        }

        if (command.EstimatedRttSeconds < 0f || command.EstimatedRttSeconds > settings.MaxReportedRttSeconds)
        {
            rejectReason = $"estimated RTT={command.EstimatedRttSeconds:F2}s is outside [0,{settings.MaxReportedRttSeconds:F2}]";
            return false;
        }

        if (command.InterpolationDelaySeconds < 0f
            || command.InterpolationDelaySeconds > settings.MaxReportedInterpolationDelaySeconds)
        {
            rejectReason = $"interpolation delay={command.InterpolationDelaySeconds:F2}s is outside [0,{settings.MaxReportedInterpolationDelaySeconds:F2}]";
            return false;
        }

        float aimDistanceX = command.AimX - shooter.X;
        float aimDistanceZ = command.AimZ - shooter.Z;
        float aimDistance = MathF.Sqrt(aimDistanceX * aimDistanceX + aimDistanceZ * aimDistanceZ);

        if (aimDistance > settings.MaxInputAimDistanceMeters)
        {
            rejectReason = $"requested aim is {aimDistance:F2}m from the authoritative player position";
            return false;
        }

        rejectReason = string.Empty;
        return true;
    }

    private bool IsRequestedAimReasonable(float serverKnownAimX, float serverKnownAimZ, FireCommand command)
    {
        float aimDx = command.AimX - serverKnownAimX;
        float aimDz = command.AimZ - serverKnownAimZ;
        float aimDistance = MathF.Sqrt(aimDx * aimDx + aimDz * aimDz);
        return aimDistance <= settings.AimToleranceMeters;
    }

    private CommandGateResult RejectInput(string reason)
    {
        RejectedInputCount++;
        IncrementRejectionCount(inputRejectionsByReason, reason);
        return CommandGateResult.Rejected(reason);
    }

    private CommandGateResult RejectFire(string reasonKey)
    {
        RejectedFireRequestCount++;
        IncrementRejectionCount(fireRejectionsByReason, reasonKey);
        return CommandGateResult.Rejected(reasonKey);
    }

    private FireReceiptDecision RememberFireReceipt(PlayerState shooter, int fireSequence, CommandGateResult result)
    {
        FireReceiptDecision receipt = CreateFireReceipt(fireSequence, result);
        shooter.FireReceipts.Add(fireSequence, receipt);
        shooter.FireReceiptSequenceOrder.Enqueue(fireSequence);

        int historyCapacity = Math.Max(1, settings.FireReceiptHistoryCapacity);

        while (shooter.FireReceiptSequenceOrder.Count > historyCapacity)
        {
            int expiredSequence = shooter.FireReceiptSequenceOrder.Dequeue();
            shooter.FireReceipts.Remove(expiredSequence);
        }

        return receipt;
    }

    private FireReceiptDecision CreateFireReceipt(int fireSequence, CommandGateResult result)
    {
        return new FireReceiptDecision(fireSequence, result.IsAccepted, result.Reason, ServerTick, false);
    }

    private static void IncrementRejectionCount(Dictionary<string, long> counters, string reason)
    {
        counters.TryGetValue(reason, out long count);
        counters[reason] = count + 1;
    }

    private static string FormatRejectionSummary(IReadOnlyDictionary<string, long> counters)
    {
        return counters.Count == 0
            ? "none"
            : string.Join(", ", counters.OrderBy(pair => pair.Key).Select(pair => $"{pair.Key}={pair.Value}"));
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    private FireRay BuildFireRay(float shooterX, float shooterZ, float shooterBodyYaw, FireCommand command)
    {
        float directionX = command.AimX - shooterX;
        float directionZ = command.AimZ - shooterZ;
        float length = MathF.Sqrt(directionX * directionX + directionZ * directionZ);

        if (length < 0.001f)
        {
            float yawRadians = DegreesToRadians(shooterBodyYaw);
            directionX = MathF.Sin(yawRadians);
            directionZ = MathF.Cos(yawRadians);
            length = 1f;
        }

        directionX /= length;
        directionZ /= length;

        return new FireRay
        {
            OriginX = shooterX + directionX * settings.MuzzleForwardOffsetMeters,
            OriginY = settings.MuzzleHeightMeters,
            OriginZ = shooterZ + directionZ * settings.MuzzleForwardOffsetMeters,
            DirectionX = directionX,
            DirectionZ = directionZ
        };
    }

    private int FindFirstHitPlayer(int shooterPlayerId, FireRay fireRay, int hitTestServerTick, out float hitDistance)
    {
        int hitPlayerId = 0;
        hitDistance = settings.FireRange;

        foreach (PlayerState target in playersById.Values)
        {
            if (target.PlayerId == shooterPlayerId || !target.IsAlive)
            {
                continue;
            }

            PlayerHistoryFrame targetFrame = GetPlayerFrameAtOrBefore(target, hitTestServerTick);

            if (!targetFrame.IsAlive)
            {
                continue;
            }

            if (TryRayCircleHit(fireRay, targetFrame.X, targetFrame.Z, out float candidateDistance) && candidateDistance < hitDistance)
            {
                hitPlayerId = target.PlayerId;
                hitDistance = candidateDistance;
            }
        }

        return hitPlayerId;
    }

    private bool TryRayCircleHit(FireRay fireRay, float targetXPosition, float targetZPosition, out float hitDistance)
    {
        Ray2D ray = new Ray2D(
            new Vec2D(fireRay.OriginX, fireRay.OriginZ),
            new Vec2D(fireRay.DirectionX, fireRay.DirectionZ));
        Circle2D target = new Circle2D(
            new Vec2D(targetXPosition, targetZPosition),
            settings.HitRadius);
        RaycastResult2D query = CollisionQueries2D.Raycast(
            ray,
            target,
            settings.FireRange,
            collisionWorld.Map.Epsilon);
        hitDistance = query.Hit ? query.Distance : 0f;
        return query.Hit;
    }

    private static float DegreesToRadians(float degrees)
    {
        return degrees * MathF.PI / 180f;
    }

    private static float RadiansToDegrees(float radians)
    {
        return radians * 180f / MathF.PI;
    }
}

public readonly struct InputCommand
{
    public InputCommand(int inputTick, float moveAxis, float turnAxis, float aimX, float aimZ)
    {
        InputTick = inputTick;
        MoveAxis = moveAxis;
        TurnAxis = turnAxis;
        AimX = aimX;
        AimZ = aimZ;
    }

    public int InputTick { get; }
    public float MoveAxis { get; }
    public float TurnAxis { get; }
    public float AimX { get; }
    public float AimZ { get; }
}

public readonly struct CommandGateResult
{
    private CommandGateResult(bool isAccepted, string reason)
    {
        IsAccepted = isAccepted;
        Reason = reason;
    }

    public bool IsAccepted { get; }
    public string Reason { get; }

    public static CommandGateResult Accepted(string reason)
    {
        return new CommandGateResult(true, reason);
    }

    public static CommandGateResult Rejected(string reason)
    {
        return new CommandGateResult(false, reason);
    }
}

public readonly struct FireCommand
{
    public FireCommand(
        int fireSequence,
        int requestTick,
        float aimX,
        float aimZ,
        float estimatedRttSeconds,
        float interpolationDelaySeconds)
    {
        FireSequence = fireSequence;
        RequestTick = requestTick;
        AimX = aimX;
        AimZ = aimZ;
        EstimatedRttSeconds = estimatedRttSeconds;
        InterpolationDelaySeconds = interpolationDelaySeconds;
    }

    public int FireSequence { get; }
    public int RequestTick { get; }
    public float AimX { get; }
    public float AimZ { get; }
    public float EstimatedRttSeconds { get; }
    public float InterpolationDelaySeconds { get; }
}

public readonly struct FireReceiptDecision
{
    public FireReceiptDecision(int fireSequence, bool accepted, string reason, int serverTick, bool isDuplicate)
    {
        FireSequence = fireSequence;
        Accepted = accepted;
        Reason = reason;
        ServerTick = serverTick;
        IsDuplicate = isDuplicate;
    }

    public int FireSequence { get; }
    public bool Accepted { get; }
    public string Reason { get; }
    public int ServerTick { get; }
    public bool IsDuplicate { get; }

    public FireReceiptDecision AsDuplicate()
    {
        return new FireReceiptDecision(FireSequence, Accepted, Reason, ServerTick, true);
    }

    public CommandGateResult ToCommandGateResult()
    {
        return Accepted ? CommandGateResult.Accepted(Reason) : CommandGateResult.Rejected(Reason);
    }
}

public sealed class PlayerState
{
    public int PlayerId { get; internal set; }
    public float X { get; internal set; }
    public float Y { get; internal set; }
    public float Z { get; internal set; }
    public float BodyYaw { get; internal set; }
    public float AimX { get; internal set; }
    public float AimZ { get; internal set; }
    public int LastProcessedInputTick { get; internal set; }
    public int Health { get; internal set; }
    public bool IsAlive { get; internal set; }
    public int LifeStateVersion { get; internal set; }
    public int LastCombatServerTick { get; internal set; }
    internal int NextAllowedFireServerTick { get; set; }
    internal int RespawnServerTick { get; set; }
    internal int LastConsumedInputServerTick { get; set; }
    internal int LastQueuedFireSequence { get; set; }
    internal int LastProcessedFireSequence { get; set; }
    internal InputCommand LatestInput { get; set; }
    internal SortedDictionary<int, InputCommand> PendingInputs { get; } = new SortedDictionary<int, InputCommand>();
    internal SortedDictionary<int, FireCommand> PendingFires { get; } = new SortedDictionary<int, FireCommand>();
    internal Dictionary<int, FireReceiptDecision> FireReceipts { get; } = new Dictionary<int, FireReceiptDecision>();
    internal Queue<int> FireReceiptSequenceOrder { get; } = new Queue<int>();
    internal List<PlayerHistoryFrame> History { get; } = new List<PlayerHistoryFrame>();
}

public abstract class GameWorldEvent
{
    public int ServerTick { get; init; }
}

public sealed class FireResolvedEvent : GameWorldEvent
{
    public int ShooterPlayerId { get; init; }
    public int FireSequence { get; init; }
    public int RequestTick { get; init; }
    public float OriginX { get; init; }
    public float OriginY { get; init; }
    public float OriginZ { get; init; }
    public float DirectionX { get; init; }
    public float DirectionY { get; init; }
    public float DirectionZ { get; init; }
    public float Range { get; init; }
    public bool LagCompensated { get; init; }
    public int HitTestServerTick { get; init; }
    public float RewindSeconds { get; init; }
}

public sealed class HitResolvedEvent : GameWorldEvent
{
    public int ShooterPlayerId { get; init; }
    public int TargetPlayerId { get; init; }
    public float HitX { get; init; }
    public float HitY { get; init; }
    public float HitZ { get; init; }
    public int Damage { get; init; }
}

public sealed class HealthChangedWorldEvent : GameWorldEvent
{
    public int PlayerId { get; init; }
    public int Health { get; init; }
    public int MaxHealth { get; init; }
    public bool IsAlive { get; init; }
    public float RespawnRemainingSeconds { get; init; }
}

public sealed class FireResultWorldEvent : GameWorldEvent
{
    public int ShooterPlayerId { get; init; }
    public int FireSequence { get; init; }
    public string Result { get; init; } = string.Empty;
    // Zero means no target was hit and is omitted from the JSON protocol.
    public int TargetPlayerId { get; init; }
}

public sealed class DeathWorldEvent : GameWorldEvent
{
    public int PlayerId { get; init; }
    public int LifeStateVersion { get; init; }
    public int KillerPlayerId { get; init; }
    public float RespawnRemainingSeconds { get; init; }
}

public sealed class RespawnWorldEvent : GameWorldEvent
{
    public int PlayerId { get; init; }
    public int LifeStateVersion { get; init; }
    public float X { get; init; }
    public float Y { get; init; }
    public float Z { get; init; }
    public int Health { get; init; }
    public int MaxHealth { get; init; }
}

public sealed class KillWorldEvent : GameWorldEvent
{
    public int KillerPlayerId { get; init; }
    public int VictimPlayerId { get; init; }
}

public sealed class MatchEndWorldEvent : GameWorldEvent
{
    public int WinnerPlayerId { get; init; }
}

internal struct FireRay
{
    public float OriginX { get; set; }
    public float OriginY { get; set; }
    public float OriginZ { get; set; }
    public float DirectionX { get; set; }
    public float DirectionZ { get; set; }
}

internal struct PlayerHistoryFrame
{
    public int ServerTick { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public float BodyYaw { get; set; }
    public float AimX { get; set; }
    public float AimZ { get; set; }
    public bool IsAlive { get; set; }
}

internal struct LagCompensationFrame
{
    public int HitTestServerTick { get; set; }
    public int RewindTicks { get; set; }
    public float RewindSeconds { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public float BodyYaw { get; set; }
    public float AimX { get; set; }
    public float AimZ { get; set; }
}
