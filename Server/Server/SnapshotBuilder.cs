// Builds protocol snapshots from the world without changing authoritative state.
public sealed class SnapshotBuilder
{
    // This is the only point where replication reads GameWorld. The resulting candidates
    // contain no UDP endpoint, JSON, or transport detail.
    public IReadOnlyList<ReplicationCandidate> Capture(GameWorld world)
    {
        return world.Players
            .OrderBy(player => player.PlayerId)
            .Select(player => new ReplicationCandidate
            {
                State = CreatePlayerState(world, player),
                LastCombatServerTick = player.LastCombatServerTick
            })
            .ToArray();
    }

    // Kept as an explicit full-world builder for diagnostics and compatibility. Normal
    // network sends now flow through ClientReplicator.BuildSnapshot per connection.
    public WorldSnapshotMessage Build(GameWorld world)
    {
        PlayerSnapshotMessage[] players = Capture(world).Select(candidate => candidate.State).ToArray();

        return new WorldSnapshotMessage
        {
            Type = "WorldSnapshot",
            ServerTick = world.ServerTick,
            Players = players,
            ReplicatedPlayerIds = players.Select(player => player.PlayerId).ToArray(),
            IsFullState = true
        };
    }

    private static PlayerSnapshotMessage CreatePlayerState(GameWorld world, PlayerState player)
    {
        return new PlayerSnapshotMessage
        {
            PlayerId = player.PlayerId,
            X = player.X,
            Y = player.Y,
            Z = player.Z,
            BodyYaw = player.BodyYaw,
            AimX = player.AimX,
            AimZ = player.AimZ,
            LastProcessedInputTick = player.LastProcessedInputTick,
            Health = player.Health,
            MaxHealth = world.MaxHealth,
            IsAlive = player.IsAlive,
            RespawnRemainingSeconds = world.GetRespawnRemainingSeconds(player),
            ChangeMask = SnapshotChangeMasks.All
        };
    }
}
