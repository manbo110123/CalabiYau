using System.Text.Json.Serialization;

public sealed class ClientHelloMessage
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
}

public sealed class ServerWelcomeMessage
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("playerId")]
    public int PlayerId { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    // The client uses this for calculations expressed in server ticks, such as
    // remote interpolation and estimating the current server Tick.
    [JsonPropertyName("serverTickRate")]
    public int ServerTickRate { get; set; }
}

public sealed class PlayerInputMessage
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("playerId")]
    public int PlayerId { get; set; }

    [JsonPropertyName("inputTick")]
    public int InputTick { get; set; }

    [JsonPropertyName("moveAxis")]
    public float MoveAxis { get; set; }

    [JsonPropertyName("turnAxis")]
    public float TurnAxis { get; set; }

    [JsonPropertyName("aimX")]
    public float AimX { get; set; }

    [JsonPropertyName("aimZ")]
    public float AimZ { get; set; }

}

public sealed class FireRequestMessage
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("playerId")]
    public int PlayerId { get; set; }

    [JsonPropertyName("fireSequence")]
    public int FireSequence { get; set; }

    [JsonPropertyName("requestTick")]
    public int RequestTick { get; set; }

    [JsonPropertyName("aimX")]
    public float AimX { get; set; }

    [JsonPropertyName("aimZ")]
    public float AimZ { get; set; }

    [JsonPropertyName("estimatedRttSeconds")]
    public float EstimatedRttSeconds { get; set; }

    [JsonPropertyName("interpolationDelaySeconds")]
    public float InterpolationDelaySeconds { get; set; }
}

// This confirms that the server received and made a queueing decision for a
// FireRequest. It deliberately says nothing about hit detection or damage,
// which are still resolved later by the authoritative GameWorld Tick.
public sealed class FireReceiptMessage
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("playerId")]
    public int PlayerId { get; set; }

    [JsonPropertyName("fireSequence")]
    public int FireSequence { get; set; }

    [JsonPropertyName("accepted")]
    public bool Accepted { get; set; }

    [JsonPropertyName("reason")]
    public string Reason { get; set; } = string.Empty;

    [JsonPropertyName("serverTick")]
    public int ServerTick { get; set; }
}

public sealed class WorldSnapshotMessage
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("serverTick")]
    public int ServerTick { get; set; }

    // A per-client datagram sequence is intentionally separate from simulation Tick.
    [JsonPropertyName("snapshotSequence")]
    public uint SnapshotSequence { get; set; }

    [JsonPropertyName("players")]
    public PlayerSnapshotMessage[] Players { get; set; } = Array.Empty<PlayerSnapshotMessage>();

    // Contains every entity which remains in this client's replication scope, including
    // low-frequency entities that did not receive a state update in this packet.
    [JsonPropertyName("replicatedPlayerIds")]
    public int[] ReplicatedPlayerIds { get; set; } = Array.Empty<int>();

    // Delta state is intentionally not enabled until a client can acknowledge a baseline.
    // Keeping this flag on the wire makes that future protocol change explicit.
    [JsonPropertyName("isFullState")]
    public bool IsFullState { get; set; } = true;
}

public sealed class ClientGoodbyeMessage
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;
}

public sealed class PlayerSnapshotMessage
{
    [JsonPropertyName("playerId")]
    public int PlayerId { get; set; }

    [JsonPropertyName("x")]
    public float X { get; set; }

    [JsonPropertyName("y")]
    public float Y { get; set; }

    [JsonPropertyName("z")]
    public float Z { get; set; }

    [JsonPropertyName("bodyYaw")]
    public float BodyYaw { get; set; }

    [JsonPropertyName("aimX")]
    public float AimX { get; set; }

    [JsonPropertyName("aimZ")]
    public float AimZ { get; set; }

    [JsonPropertyName("lastProcessedInputTick")]
    public int LastProcessedInputTick { get; set; }

    [JsonPropertyName("health")]
    public int Health { get; set; }

    [JsonPropertyName("maxHealth")]
    public int MaxHealth { get; set; }

    [JsonPropertyName("isAlive")]
    public bool IsAlive { get; set; }

    // Increments only when this player's life state changes (death or respawn).
    // It lets a client reject a delayed lifecycle event even when UDP reorders it.
    [JsonPropertyName("lifeStateVersion")]
    public int LifeStateVersion { get; set; }

    [JsonPropertyName("respawnRemainingSeconds")]
    public float RespawnRemainingSeconds { get; set; }

    // Phase 11 reserves the field-level delta interface. While IsFullState is true this
    // is always All, so UDP packet loss can never make a client depend on a prior packet.
    [JsonPropertyName("changeMask")]
    public uint ChangeMask { get; set; } = SnapshotChangeMasks.All;
}

public static class SnapshotChangeMasks
{
    public const uint Position = 1 << 0;
    public const uint BodyYaw = 1 << 1;
    public const uint Aim = 1 << 2;
    public const uint LastProcessedInputTick = 1 << 3;
    public const uint Health = 1 << 4;
    public const uint LifeState = 1 << 5;
    public const uint Respawn = 1 << 6;
    public const uint All = Position | BodyYaw | Aim | LastProcessedInputTick | Health | LifeState | Respawn;
}

public sealed class FireEventMessage
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("serverTick")]
    public int ServerTick { get; set; }

    [JsonPropertyName("shooterPlayerId")]
    public int ShooterPlayerId { get; set; }

    // This stays on the ordinary FireEvent so a local client can avoid replaying its
    // own immediate muzzle effect when the authoritative presentation event arrives.
    [JsonPropertyName("fireSequence")]
    public int FireSequence { get; set; }

    [JsonPropertyName("requestTick")]
    public int RequestTick { get; set; }

    [JsonPropertyName("originX")]
    public float OriginX { get; set; }

    [JsonPropertyName("originY")]
    public float OriginY { get; set; }

    [JsonPropertyName("originZ")]
    public float OriginZ { get; set; }

    [JsonPropertyName("directionX")]
    public float DirectionX { get; set; }

    [JsonPropertyName("directionY")]
    public float DirectionY { get; set; }

    [JsonPropertyName("directionZ")]
    public float DirectionZ { get; set; }

    [JsonPropertyName("range")]
    public float Range { get; set; }

    [JsonPropertyName("lagCompensated")]
    public bool LagCompensated { get; set; }

    [JsonPropertyName("hitTestServerTick")]
    public int HitTestServerTick { get; set; }

    [JsonPropertyName("rewindSeconds")]
    public float RewindSeconds { get; set; }
}

// A reliable, final answer for one previously accepted FireRequest. Unlike a
// FireReceipt, this is emitted only after GameWorld.Tick has resolved cooldown,
// hit detection, and any resulting damage.
public sealed class FireResultMessage
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("eventId")]
    public long EventId { get; set; }

    [JsonPropertyName("serverTick")]
    public int ServerTick { get; set; }

    [JsonPropertyName("playerId")]
    public int PlayerId { get; set; }

    [JsonPropertyName("fireSequence")]
    public int FireSequence { get; set; }

    [JsonPropertyName("result")]
    public string Result { get; set; } = string.Empty;

    // A target is useful only for fired-hit. Omitting the default keeps no-hit and
    // rejection results focused on their own fireSequence and final decision.
    [JsonPropertyName("targetPlayerId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int TargetPlayerId { get; set; }
}

public sealed class HitEventMessage
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("serverTick")]
    public int ServerTick { get; set; }

    [JsonPropertyName("shooterPlayerId")]
    public int ShooterPlayerId { get; set; }

    [JsonPropertyName("targetPlayerId")]
    public int TargetPlayerId { get; set; }

    [JsonPropertyName("hitX")]
    public float HitX { get; set; }

    [JsonPropertyName("hitY")]
    public float HitY { get; set; }

    [JsonPropertyName("hitZ")]
    public float HitZ { get; set; }

    [JsonPropertyName("damage")]
    public int Damage { get; set; }
}

public sealed class HealthChangedEventMessage
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("serverTick")]
    public int ServerTick { get; set; }

    [JsonPropertyName("playerId")]
    public int PlayerId { get; set; }

    [JsonPropertyName("health")]
    public int Health { get; set; }

    [JsonPropertyName("maxHealth")]
    public int MaxHealth { get; set; }

    [JsonPropertyName("isAlive")]
    public bool IsAlive { get; set; }

    [JsonPropertyName("respawnRemainingSeconds")]
    public float RespawnRemainingSeconds { get; set; }
}

public sealed class EventAckMessage
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("eventId")]
    public long EventId { get; set; }
}

public sealed class DeathEventMessage
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;
    [JsonPropertyName("eventId")]
    public long EventId { get; set; }
    [JsonPropertyName("serverTick")]
    public int ServerTick { get; set; }
    [JsonPropertyName("playerId")]
    public int PlayerId { get; set; }
    [JsonPropertyName("lifeStateVersion")]
    public int LifeStateVersion { get; set; }
    [JsonPropertyName("killerPlayerId")]
    public int KillerPlayerId { get; set; }
    [JsonPropertyName("respawnRemainingSeconds")]
    public float RespawnRemainingSeconds { get; set; }
}

public sealed class RespawnEventMessage
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;
    [JsonPropertyName("eventId")]
    public long EventId { get; set; }
    [JsonPropertyName("serverTick")]
    public int ServerTick { get; set; }
    [JsonPropertyName("playerId")]
    public int PlayerId { get; set; }
    [JsonPropertyName("lifeStateVersion")]
    public int LifeStateVersion { get; set; }
    [JsonPropertyName("x")]
    public float X { get; set; }
    [JsonPropertyName("y")]
    public float Y { get; set; }
    [JsonPropertyName("z")]
    public float Z { get; set; }
    [JsonPropertyName("health")]
    public int Health { get; set; }
    [JsonPropertyName("maxHealth")]
    public int MaxHealth { get; set; }
}

public sealed class KillEventMessage
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;
    [JsonPropertyName("eventId")]
    public long EventId { get; set; }
    [JsonPropertyName("serverTick")]
    public int ServerTick { get; set; }
    [JsonPropertyName("killerPlayerId")]
    public int KillerPlayerId { get; set; }
    [JsonPropertyName("victimPlayerId")]
    public int VictimPlayerId { get; set; }
}

public sealed class MatchEndEventMessage
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;
    [JsonPropertyName("eventId")]
    public long EventId { get; set; }
    [JsonPropertyName("serverTick")]
    public int ServerTick { get; set; }
    [JsonPropertyName("winnerPlayerId")]
    public int WinnerPlayerId { get; set; }
}
