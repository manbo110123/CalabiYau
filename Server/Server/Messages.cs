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

    [JsonPropertyName("fire")]
    public bool Fire { get; set; }
}

public sealed class FireRequestMessage
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("playerId")]
    public int PlayerId { get; set; }

    [JsonPropertyName("requestTick")]
    public int RequestTick { get; set; }

    [JsonPropertyName("aimX")]
    public float AimX { get; set; }

    [JsonPropertyName("aimZ")]
    public float AimZ { get; set; }

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

    [JsonPropertyName("estimatedRttSeconds")]
    public float EstimatedRttSeconds { get; set; }

    [JsonPropertyName("interpolationDelaySeconds")]
    public float InterpolationDelaySeconds { get; set; }
}

public sealed class WorldSnapshotMessage
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("serverTick")]
    public int ServerTick { get; set; }

    [JsonPropertyName("players")]
    public PlayerSnapshotMessage[] Players { get; set; } = Array.Empty<PlayerSnapshotMessage>();
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

    [JsonPropertyName("respawnRemainingSeconds")]
    public float RespawnRemainingSeconds { get; set; }
}

public sealed class FireEventMessage
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("serverTick")]
    public int ServerTick { get; set; }

    [JsonPropertyName("shooterPlayerId")]
    public int ShooterPlayerId { get; set; }

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
