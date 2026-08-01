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
    public float MinimumHorizontalFireDirectionLength { get; set; } = 0.25f;
    public float MuzzleForwardOffsetMeters { get; set; } = 1.4f;
    public float MuzzleHeightMeters { get; set; } = 1.2f;
    public bool EnableLagCompensation { get; set; } = true;
    public float LagCompensationHistorySeconds { get; set; } = 1f;
    public float LagCompensationMaxRewindSeconds { get; set; } = 0.35f;

    // At 30 Hz this keeps about two seconds of commands. It absorbs a short burst of UDP
    // reordering without making the server retain an unbounded client-controlled queue.
    public int InputBufferCapacity { get; set; } = 64;

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
    private readonly Dictionary<int, PlayerState> playersById = new Dictionary<int, PlayerState>();
    private readonly Dictionary<string, long> inputRejectionsByReason = new Dictionary<string, long>();
    private readonly Dictionary<string, long> fireRejectionsByReason = new Dictionary<string, long>();

    public GameWorld()
        : this(new GameWorldSettings())
    {
    }

    public GameWorld(GameWorldSettings settings)
    {
        this.settings = settings;
    }

    public int ServerTick { get; private set; }
    public int MaxHealth => settings.MaxHealth;
    public IReadOnlyCollection<PlayerState> Players => playersById.Values;
    public long ReceivedInputCount { get; private set; }
    public long AcceptedInputCount { get; private set; }
    public long RejectedInputCount { get; private set; }
    public long SupersededInputCount { get; private set; }
    public long ReceivedFireRequestCount { get; private set; }
    public long AcceptedFireRequestCount { get; private set; }
    public long RejectedFireRequestCount { get; private set; }
    public long DeathCount { get; private set; }
    public long RespawnCount { get; private set; }
    public long LagCompensatedFireRequestCount { get; private set; }
    public IReadOnlyDictionary<string, long> InputRejectionsByReason => inputRejectionsByReason;
    public IReadOnlyDictionary<string, long> FireRejectionsByReason => fireRejectionsByReason;

    public bool AddPlayer(int playerId)
    {
        if (playersById.ContainsKey(playerId))
        {
            return false;
        }

        playersById.Add(playerId, CreateInitialPlayerState(playerId));
        return true;
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

    public bool TryHandleFireRequest(
        int playerId,
        FireCommand command,
        out List<GameWorldEvent> eventsToBroadcast,
        out string rejectReason)
    {
        eventsToBroadcast = new List<GameWorldEvent>();
        rejectReason = string.Empty;
        ReceivedFireRequestCount++;

        if (!playersById.TryGetValue(playerId, out PlayerState? shooter))
        {
            rejectReason = $"unknown shooter playerId={playerId}";
            return RejectFire("unknown-player");
        }

        if (!IsValidFireCommand(command, shooter, out rejectReason))
        {
            return RejectFire("invalid-fire-command");
        }

        if (!shooter.IsAlive)
        {
            rejectReason = $"playerId={playerId} is dead and waiting to respawn";
            return RejectFire("player-dead");
        }

        if (command.RequestTick < shooter.LastProcessedInputTick - settings.MaxInputTickLag)
        {
            rejectReason = $"requestTick={command.RequestTick} is older than accepted input window";
            return RejectFire("request-too-old");
        }

        if (command.RequestTick > shooter.LastProcessedInputTick + settings.MaxInputTickAhead)
        {
            rejectReason = $"requestTick={command.RequestTick} is too far ahead of lastProcessedInputTick={shooter.LastProcessedInputTick}";
            return RejectFire("request-too-far-in-future");
        }

        if (command.RequestTick <= shooter.LastHandledFireRequestTick)
        {
            rejectReason = $"requestTick={command.RequestTick} is duplicate or out of order; lastHandled={shooter.LastHandledFireRequestTick}";
            return RejectFire("duplicate-or-out-of-order-request");
        }

        // A fire intent is single-use even when it is rejected by cooldown. A delayed UDP
        // duplicate must not become a valid shot after the cooldown has elapsed.
        shooter.LastHandledFireRequestTick = command.RequestTick;

        if (ServerTick < shooter.NextAllowedFireServerTick)
        {
            rejectReason = $"cooldown is not ready until serverTick={shooter.NextAllowedFireServerTick}";
            return RejectFire("cooldown");
        }

        LagCompensationFrame shooterFrame = BuildLagCompensationFrame(shooter, command);

        if (!IsRequestedAimReasonable(shooter.AimX, shooter.AimZ, command)
            && !IsRequestedAimReasonable(shooterFrame.AimX, shooterFrame.AimZ, command))
        {
            rejectReason = "requested aim is too far from the current and lag-compensated server-known aim";
            return RejectFire("aim-mismatch");
        }

        if (!IsRequestedFireDirectionReasonable(command, out rejectReason))
        {
            return RejectFire("invalid-fire-direction");
        }

        AcceptedFireRequestCount++;
        shooter.NextAllowedFireServerTick = ServerTick + Math.Max(1, (int)MathF.Ceiling(settings.FireCooldownSeconds * settings.ServerTickRate));

        if (shooterFrame.RewindTicks > 0)
        {
            LagCompensatedFireRequestCount++;
        }

        FireRay fireRay = BuildFireRay(shooterFrame.X, shooterFrame.Z, shooterFrame.BodyYaw, command);
        int hitPlayerId = FindFirstHitPlayer(shooter.PlayerId, fireRay, shooterFrame.HitTestServerTick, out float hitDistance);

        eventsToBroadcast.Add(new FireResolvedEvent
        {
            ServerTick = ServerTick,
            ShooterPlayerId = shooter.PlayerId,
            RequestTick = command.RequestTick,
            OriginX = fireRay.OriginX,
            OriginY = fireRay.OriginY,
            OriginZ = fireRay.OriginZ,
            DirectionX = fireRay.DirectionX,
            DirectionY = 0f,
            DirectionZ = fireRay.DirectionZ,
            Range = settings.FireRange,
            LagCompensated = shooterFrame.RewindTicks > 0,
            HitTestServerTick = shooterFrame.HitTestServerTick,
            RewindSeconds = shooterFrame.RewindSeconds
        });

        if (hitPlayerId != 0 && playersById.TryGetValue(hitPlayerId, out PlayerState? hitPlayer))
        {
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
        }

        return true;
    }

    public List<GameWorldEvent> Tick(float deltaTime)
    {
        ServerTick++;
        List<GameWorldEvent> eventsToBroadcast = new List<GameWorldEvent>();

        foreach (PlayerState player in playersById.Values)
        {
            HealthChangedWorldEvent? respawnEvent = TryRespawnPlayer(player);

            if (respawnEvent != null)
            {
                eventsToBroadcast.Add(respawnEvent);
            }

            ConsumeLatestInputForTick(player);
            SimulatePlayer(player, deltaTime);
            StorePlayerHistory(player);
        }

        return eventsToBroadcast;
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

    private PlayerState CreateInitialPlayerState(int playerId)
    {
        float spawnX = (playerId - 1) * 4f;

        PlayerState player = new PlayerState
        {
            PlayerId = playerId,
            X = spawnX,
            Y = 0f,
            Z = 0f,
            BodyYaw = 0f,
            AimX = spawnX,
            AimZ = 5f,
            Health = settings.MaxHealth,
            IsAlive = true,
            NextAllowedFireServerTick = 0,
            RespawnServerTick = 0,
            LatestInput = new InputCommand(0, 0f, 0f, spawnX, 5f, false)
        };

        StorePlayerHistory(player);
        return player;
    }

    private void SimulatePlayer(PlayerState player, float deltaTime)
    {
        if (!player.IsAlive)
        {
            return;
        }

        InputCommand input = player.LatestInput;
        float moveAxis = input.MoveAxis;
        float turnAxis = input.TurnAxis;

        player.BodyYaw += turnAxis * settings.PlayerTurnDegreesPerSecond * deltaTime;

        float yawRadians = DegreesToRadians(player.BodyYaw);
        float forwardX = MathF.Sin(yawRadians);
        float forwardZ = MathF.Cos(yawRadians);

        player.X += forwardX * settings.PlayerMoveSpeed * moveAxis * deltaTime;
        player.Z += forwardZ * settings.PlayerMoveSpeed * moveAxis * deltaTime;
        player.AimX = input.AimX;
        player.AimZ = input.AimZ;
    }

    private void KillPlayer(PlayerState player)
    {
        player.IsAlive = false;
        player.Health = 0;
        player.RespawnServerTick = ServerTick + Math.Max(1, (int)MathF.Ceiling(settings.RespawnDelaySeconds * settings.ServerTickRate));
        player.NextAllowedFireServerTick = player.RespawnServerTick;
        player.LatestInput = new InputCommand(
            player.LatestInput.InputTick,
            0f,
            0f,
            player.LatestInput.AimX,
            player.LatestInput.AimZ,
            false);
        player.PendingInputs.Clear();
        DeathCount++;
    }

    private HealthChangedWorldEvent? TryRespawnPlayer(PlayerState player)
    {
        if (player.IsAlive || player.RespawnServerTick <= 0 || ServerTick < player.RespawnServerTick)
        {
            return null;
        }

        float spawnX = (player.PlayerId - 1) * 4f;
        player.X = spawnX;
        player.Y = 0f;
        player.Z = 0f;
        player.BodyYaw = 0f;
        player.AimX = spawnX;
        player.AimZ = 5f;
        player.Health = settings.MaxHealth;
        player.IsAlive = true;
        player.RespawnServerTick = 0;
        player.NextAllowedFireServerTick = ServerTick + Math.Max(1, (int)MathF.Ceiling(0.25f * settings.ServerTickRate));
        player.LatestInput = new InputCommand(player.LatestInput.InputTick, 0f, 0f, spawnX, 5f, false);
        player.PendingInputs.Clear();
        RespawnCount++;

        return CreateHealthChangedEvent(player);
    }

    // The buffer is sorted for deterministic inspection, but each server tick uses the
    // newest command currently available. This preserves the old responsive "latest input"
    // behavior while ensuring a late packet can never move the world backward in time.
    private void ConsumeLatestInputForTick(PlayerState player)
    {
        if (player.PendingInputs.Count == 0)
        {
            return;
        }

        KeyValuePair<int, InputCommand> newestInput = player.PendingInputs.Last();
        int discardedInputCount = player.PendingInputs.Count - 1;
        player.PendingInputs.Clear();
        player.LatestInput = newestInput.Value;
        player.LastProcessedInputTick = newestInput.Key;
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
        if (command.RequestTick <= 0)
        {
            rejectReason = "requestTick must be positive";
            return false;
        }

        if (!IsFinite(command.AimX)
            || !IsFinite(command.AimZ)
            || !IsFinite(command.OriginX)
            || !IsFinite(command.OriginY)
            || !IsFinite(command.OriginZ)
            || !IsFinite(command.DirectionX)
            || !IsFinite(command.DirectionY)
            || !IsFinite(command.DirectionZ)
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

    private bool IsRequestedFireDirectionReasonable(FireCommand command, out string rejectReason)
    {
        float horizontalDirectionLength = MathF.Sqrt(
            command.DirectionX * command.DirectionX +
            command.DirectionZ * command.DirectionZ);

        if (horizontalDirectionLength < settings.MinimumHorizontalFireDirectionLength)
        {
            rejectReason =
                $"requested fire direction is too vertical or too small: " +
                $"horizontalLength={horizontalDirectionLength:F2}, allowed={settings.MinimumHorizontalFireDirectionLength:F2}, " +
                $"direction=({command.DirectionX:F2},{command.DirectionY:F2},{command.DirectionZ:F2})";
            return false;
        }

        rejectReason = string.Empty;
        return true;
    }

    private CommandGateResult RejectInput(string reason)
    {
        RejectedInputCount++;
        IncrementRejectionCount(inputRejectionsByReason, reason);
        return CommandGateResult.Rejected(reason);
    }

    private bool RejectFire(string reasonKey)
    {
        RejectedFireRequestCount++;
        IncrementRejectionCount(fireRejectionsByReason, reasonKey);
        return false;
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
        float directionX = command.DirectionX;
        float directionZ = command.DirectionZ;
        float length = MathF.Sqrt(directionX * directionX + directionZ * directionZ);

        if (length < 0.001f)
        {
            directionX = command.AimX - shooterX;
            directionZ = command.AimZ - shooterZ;
            length = MathF.Sqrt(directionX * directionX + directionZ * directionZ);
        }

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
        float targetX = targetXPosition - fireRay.OriginX;
        float targetZ = targetZPosition - fireRay.OriginZ;
        float projectedDistance = targetX * fireRay.DirectionX + targetZ * fireRay.DirectionZ;

        if (projectedDistance < 0f || projectedDistance > settings.FireRange)
        {
            hitDistance = 0f;
            return false;
        }

        float closestX = fireRay.DirectionX * projectedDistance;
        float closestZ = fireRay.DirectionZ * projectedDistance;
        float distanceX = targetX - closestX;
        float distanceZ = targetZ - closestZ;
        float squaredDistance = distanceX * distanceX + distanceZ * distanceZ;

        if (squaredDistance > settings.HitRadius * settings.HitRadius)
        {
            hitDistance = 0f;
            return false;
        }

        hitDistance = projectedDistance;
        return true;
    }

    private static float DegreesToRadians(float degrees)
    {
        return degrees * MathF.PI / 180f;
    }
}

public readonly struct InputCommand
{
    public InputCommand(int inputTick, float moveAxis, float turnAxis, float aimX, float aimZ, bool fire)
    {
        InputTick = inputTick;
        MoveAxis = moveAxis;
        TurnAxis = turnAxis;
        AimX = aimX;
        AimZ = aimZ;
        Fire = fire;
    }

    public int InputTick { get; }
    public float MoveAxis { get; }
    public float TurnAxis { get; }
    public float AimX { get; }
    public float AimZ { get; }
    public bool Fire { get; }
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
        int requestTick,
        float aimX,
        float aimZ,
        float originX,
        float originY,
        float originZ,
        float directionX,
        float directionY,
        float directionZ,
        float estimatedRttSeconds,
        float interpolationDelaySeconds)
    {
        RequestTick = requestTick;
        AimX = aimX;
        AimZ = aimZ;
        OriginX = originX;
        OriginY = originY;
        OriginZ = originZ;
        DirectionX = directionX;
        DirectionY = directionY;
        DirectionZ = directionZ;
        EstimatedRttSeconds = estimatedRttSeconds;
        InterpolationDelaySeconds = interpolationDelaySeconds;
    }

    public int RequestTick { get; }
    public float AimX { get; }
    public float AimZ { get; }
    public float OriginX { get; }
    public float OriginY { get; }
    public float OriginZ { get; }
    public float DirectionX { get; }
    public float DirectionY { get; }
    public float DirectionZ { get; }
    public float EstimatedRttSeconds { get; }
    public float InterpolationDelaySeconds { get; }
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
    internal int NextAllowedFireServerTick { get; set; }
    internal int RespawnServerTick { get; set; }
    internal int LastHandledFireRequestTick { get; set; }
    internal InputCommand LatestInput { get; set; }
    internal SortedDictionary<int, InputCommand> PendingInputs { get; } = new SortedDictionary<int, InputCommand>();
    internal List<PlayerHistoryFrame> History { get; } = new List<PlayerHistoryFrame>();
}

public abstract class GameWorldEvent
{
    public int ServerTick { get; init; }
}

public sealed class FireResolvedEvent : GameWorldEvent
{
    public int ShooterPlayerId { get; init; }
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
