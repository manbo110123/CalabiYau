// Builds protocol snapshots from the world without changing authoritative state.
public sealed class SnapshotBuilder
{
    public WorldSnapshotMessage Build(GameWorld world)
    {
        PlayerSnapshotMessage[] players = world.Players
            .OrderBy(player => player.PlayerId)
            .Select(player => new PlayerSnapshotMessage
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
                RespawnRemainingSeconds = world.GetRespawnRemainingSeconds(player)
            })
            .ToArray();

        return new WorldSnapshotMessage
        {
            Type = "WorldSnapshot",
            ServerTick = world.ServerTick,
            Players = players
        };
    }
}
