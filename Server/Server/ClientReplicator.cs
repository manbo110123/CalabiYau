// Owns the state-replication policy for exactly one connected client. It does not
// change GameWorld; it only decides which already-authoritative state is sent.
public sealed class ClientReplicationSettings
{
    // Simulation remains 30 Hz by default, while snapshots are sent at this independent rate.
    public int SnapshotRate { get; set; } = 30;

    // Distance filtering is the phase-11 default. Disable it for the explicit chapter-one
    // full-snapshot comparison mode.
    public bool EnableDistanceFiltering { get; set; } = true;

    public float HighPriorityDistanceMeters { get; set; } = 18f;
    public float ReplicationDistanceMeters { get; set; } = 45f;
    public int HighPrioritySnapshotRate { get; set; } = 30;
    public int LowPrioritySnapshotRate { get; set; } = 5;

    // A player which has just fired or been hit stays high priority briefly, even at range.
    public int CombatPriorityDurationTicks { get; set; } = 30;

}

public sealed class ReplicationCandidate
{
    public required PlayerSnapshotMessage State { get; init; }
    public int LastCombatServerTick { get; init; }
}

public sealed class ClientSnapshotPlan
{
    public required WorldSnapshotMessage Snapshot { get; init; }
    public int ScopedPlayerCount { get; init; }
    public int HighPriorityPlayerCount { get; init; }
    public int LowPriorityPlayerCount { get; init; }
}

public readonly struct ClientReplicationTelemetry
{
    public ClientReplicationTelemetry(int snapshotCount, long byteCount, int lastPlayerCount, int lastScopedPlayerCount)
    {
        SnapshotCount = snapshotCount;
        ByteCount = byteCount;
        LastPlayerCount = lastPlayerCount;
        LastScopedPlayerCount = lastScopedPlayerCount;
    }

    public int SnapshotCount { get; }
    public long ByteCount { get; }
    public int LastPlayerCount { get; }
    public int LastScopedPlayerCount { get; }
}

public sealed class ClientReplicator
{
    private readonly ClientReplicationSettings settings;
    private readonly Dictionary<int, int> lastSentTickByPlayerId = new Dictionary<int, int>();
    private int sentSnapshotCountSinceTelemetry;
    private uint nextSnapshotSequence;
    private long sentBytesSinceTelemetry;
    private int lastSentPlayerCount;
    private int lastScopedPlayerCount;

    public ClientReplicator(ClientReplicationSettings settings)
    {
        this.settings = settings;
    }

    public ClientSnapshotPlan BuildSnapshot(int recipientPlayerId, int serverTick, int serverTickRate, IReadOnlyList<ReplicationCandidate> candidates)
    {
        ReplicationCandidate? recipient = candidates.SingleOrDefault(candidate => candidate.State.PlayerId == recipientPlayerId);

        if (recipient == null)
        {
            throw new InvalidOperationException($"Cannot build a snapshot for unknown recipient playerId={recipientPlayerId}.");
        }

        List<PlayerSnapshotMessage> statesToSend = new List<PlayerSnapshotMessage>();
        List<int> scopedPlayerIds = new List<int>();
        HashSet<int> existingPlayerIds = new HashSet<int>();
        int highPriorityCount = 0;
        int lowPriorityCount = 0;

        foreach (ReplicationCandidate candidate in candidates.OrderBy(candidate => candidate.State.PlayerId))
        {
            PlayerSnapshotMessage state = candidate.State;
            existingPlayerIds.Add(state.PlayerId);

            bool isRecipient = state.PlayerId == recipientPlayerId;
            float distance = GetGroundDistance(recipient.State, state);
            bool isInRange = !settings.EnableDistanceFiltering || isRecipient || distance <= settings.ReplicationDistanceMeters;

            if (!isInRange)
            {
                lastSentTickByPlayerId.Remove(state.PlayerId);
                continue;
            }

            scopedPlayerIds.Add(state.PlayerId);

            bool isHighPriority = isRecipient
                || !settings.EnableDistanceFiltering
                || distance <= settings.HighPriorityDistanceMeters
                || (candidate.LastCombatServerTick > 0
                    && serverTick - candidate.LastCombatServerTick <= settings.CombatPriorityDurationTicks);

            if (isHighPriority)
            {
                highPriorityCount++;
            }
            else
            {
                lowPriorityCount++;
            }

            if (ShouldSendState(state.PlayerId, serverTick, serverTickRate, isRecipient, isHighPriority))
            {
                // Full state is deliberate: no UDP packet is used as an unconfirmed baseline.
                statesToSend.Add(CloneAsFullState(state));
                lastSentTickByPlayerId[state.PlayerId] = serverTick;
            }
        }

        foreach (int playerId in lastSentTickByPlayerId.Keys.Where(playerId => !existingPlayerIds.Contains(playerId)).ToArray())
        {
            lastSentTickByPlayerId.Remove(playerId);
        }

        return new ClientSnapshotPlan
        {
            Snapshot = new WorldSnapshotMessage
            {
                Type = "WorldSnapshot",
                ServerTick = serverTick,
                SnapshotSequence = ++nextSnapshotSequence,
                Players = statesToSend.ToArray(),
                ReplicatedPlayerIds = scopedPlayerIds.ToArray(),
                // Delta snapshots need a client-acknowledged baseline. Until that protocol
                // exists, every emitted state entry is explicitly a full authoritative state.
                IsFullState = true
            },
            ScopedPlayerCount = scopedPlayerIds.Count,
            HighPriorityPlayerCount = highPriorityCount,
            LowPriorityPlayerCount = lowPriorityCount
        };
    }

    public bool IsPlayerInReplicationScope(int recipientPlayerId, int subjectPlayerId, IReadOnlyList<ReplicationCandidate> candidates)
    {
        ReplicationCandidate? recipient = candidates.SingleOrDefault(candidate => candidate.State.PlayerId == recipientPlayerId);
        ReplicationCandidate? subject = candidates.SingleOrDefault(candidate => candidate.State.PlayerId == subjectPlayerId);

        if (recipient == null || subject == null)
        {
            return false;
        }

        return !settings.EnableDistanceFiltering
            || recipientPlayerId == subjectPlayerId
            || GetGroundDistance(recipient.State, subject.State) <= settings.ReplicationDistanceMeters;
    }

    public void RecordSentSnapshot(int byteCount, ClientSnapshotPlan plan)
    {
        sentSnapshotCountSinceTelemetry++;
        sentBytesSinceTelemetry += byteCount;
        lastSentPlayerCount = plan.Snapshot.Players.Length;
        lastScopedPlayerCount = plan.ScopedPlayerCount;
    }

    public ClientReplicationTelemetry ConsumeTelemetry()
    {
        ClientReplicationTelemetry telemetry = new ClientReplicationTelemetry(
            sentSnapshotCountSinceTelemetry,
            sentBytesSinceTelemetry,
            lastSentPlayerCount,
            lastScopedPlayerCount);
        sentSnapshotCountSinceTelemetry = 0;
        sentBytesSinceTelemetry = 0;
        return telemetry;
    }

    private bool ShouldSendState(int playerId, int serverTick, int serverTickRate, bool isRecipient, bool isHighPriority)
    {
        // The owner is always included. Its input acknowledgement and life state drive
        // reconciliation, so it must never be paced behind a remote entity.
        if (isRecipient || !lastSentTickByPlayerId.TryGetValue(playerId, out int lastSentTick))
        {
            return true;
        }

        int requestedRate = isHighPriority ? settings.HighPrioritySnapshotRate : settings.LowPrioritySnapshotRate;

        // The outer server loop already limits snapshots to SnapshotRate. A high-priority
        // entity at that rate should appear in every outgoing snapshot, even when the two
        // rates do not divide evenly into the simulation tick rate.
        if (isHighPriority && requestedRate >= settings.SnapshotRate)
        {
            return true;
        }

        int safeRate = Math.Clamp(requestedRate, 1, Math.Max(1, serverTickRate));
        int intervalTicks = Math.Max(1, (int)Math.Ceiling(serverTickRate / (double)safeRate));
        return serverTick - lastSentTick >= intervalTicks;
    }

    private static PlayerSnapshotMessage CloneAsFullState(PlayerSnapshotMessage state)
    {
        return new PlayerSnapshotMessage
        {
            PlayerId = state.PlayerId,
            CharacterMovement = state.CharacterMovement,
            ShoulderHeld = state.ShoulderHeld,
            X = state.X,
            Y = state.Y,
            Z = state.Z,
            BodyYaw = state.BodyYaw,
            AimX = state.AimX,
            AimZ = state.AimZ,
            LastProcessedInputTick = state.LastProcessedInputTick,
            Health = state.Health,
            MaxHealth = state.MaxHealth,
            IsAlive = state.IsAlive,
            LifeStateVersion = state.LifeStateVersion,
            RespawnRemainingSeconds = state.RespawnRemainingSeconds,
            ChangeMask = SnapshotChangeMasks.All
        };
    }

    private static float GetGroundDistance(PlayerSnapshotMessage a, PlayerSnapshotMessage b)
    {
        float dx = a.X - b.X;
        float dz = a.Z - b.Z;
        return MathF.Sqrt(dx * dx + dz * dz);
    }
}
