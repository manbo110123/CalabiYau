using CalabiYau.CollisionCore;
using CalabiYau.TankCollision;

internal static class GameWorldAuthoritativeCollisionTests
{
    public static void RunAll()
    {
        RepeatedForwardCommandsCannotCrossAuthoritativeWall();
        AuthoritativeDiagonalMovementSlidesAlongWall();
        AuthoritativeRotationCannotInsertTankIntoWall();
        DefaultTrainingMapBlocksItsProjectedInclinedCube();
    }

    private static void RepeatedForwardCommandsCannotCrossAuthoritativeWall()
    {
        GameWorld world = CreateWorldWithWall(
            new Aabb2D(new Vec2D(0f, 2f), new Vec2D(6f, 0.1f)),
            new Vec2D(0.5f, 0.75f));
        CollisionCoreTests.Assert(world.AddPlayer(1), "collision test player must be added");

        for (int tick = 1; tick <= 120; tick++)
        {
            CommandGateResult gate = world.TryQueueInput(
                1,
                new InputCommand(tick, 1f, 0f, 0f, 10f));
            CollisionCoreTests.Assert(gate.IsAccepted, $"wall-push input tick {tick} must be accepted");
            world.Tick(1f / 30f);
        }

        PlayerState player = world.Players.Single();
        CollisionCoreTests.AssertNear(
            player.Z,
            1.1f,
            "authoritative repeated wall-push stop position",
            0.0003f);
        CollisionCoreTests.Assert(
            world.BlockedMovementTickCount > 0,
            "authority must count blocked movement ticks");
        CollisionCoreTests.Assert(
            world.CollisionResolutionCount > 0,
            "authority must perform collision resolution");
    }

    private static void AuthoritativeDiagonalMovementSlidesAlongWall()
    {
        GameWorld world = CreateWorldWithWall(
            new Aabb2D(new Vec2D(2f, 0f), new Vec2D(0.1f, 12f)),
            new Vec2D(0.5f, 0.75f));
        CollisionCoreTests.Assert(world.AddPlayer(1), "diagonal collision test player must be added");
        PlayerState player = world.Players.Single();
        player.BodyYaw = 45f;

        for (int tick = 1; tick <= 30; tick++)
        {
            CollisionCoreTests.Assert(
                world.TryQueueInput(1, new InputCommand(tick, 1f, 0f, 10f, 10f)).IsAccepted,
                $"diagonal input tick {tick} must be accepted");
            world.Tick(1f / 30f);
        }

        CollisionCoreTests.Assert(player.X < 1f, "authoritative wall must block diagonal X travel");
        CollisionCoreTests.Assert(player.Z > 4f, "authoritative slide must preserve tangent Z travel");
    }

    private static void AuthoritativeRotationCannotInsertTankIntoWall()
    {
        GameWorld world = CreateWorldWithWall(
            new Aabb2D(new Vec2D(2f, 0f), new Vec2D(0.1f, 8f)),
            new Vec2D(0.4f, 1.2f));
        CollisionCoreTests.Assert(world.AddPlayer(1), "rotation collision test player must be added");
        PlayerState player = world.Players.Single();
        player.X = 0.7f;

        CollisionCoreTests.Assert(
            world.TryQueueInput(1, new InputCommand(1, 0f, 1f, 0f, 5f)).IsAccepted,
            "near-wall turn input must be accepted at the command gate");
        world.Tick(0.5f);

        CollisionCoreTests.Assert(
            player.BodyYaw > 0f && player.BodyYaw < 90f,
            "authority may accept safe micro-rotation but must stop before the requested wall insertion");
        CollisionCoreTests.Assert(
            world.BlockedRotationTickCount == 1,
            "authority must report the rejected near-wall rotation");
    }

    private static void DefaultTrainingMapBlocksItsProjectedInclinedCube()
    {
        GameWorld world = new GameWorld(new GameWorldSettings());
        CollisionCoreTests.Assert(world.AddPlayer(1), "default-map collision test player must be added");
        PlayerState player = world.Players.Single();

        // At yaw zero, the Tank body center is root + (-1, +0.92). This aligns its
        // center X with SampleScene/Cube (1) and starts just before its projected face.
        player.X = 5.63f;
        player.Z = 2f;

        CollisionCoreTests.Assert(
            world.TryQueueInput(1, new InputCommand(1, 1f, 0f, 5.63f, 10f)).IsAccepted,
            "default-map forward input must be accepted");
        world.Tick(1f / 30f);

        CollisionCoreTests.Assert(
            player.Z < 2.23f,
            "default SampleScene map must stop the Tank before the projected inclined Cube");
        CollisionCoreTests.Assert(
            world.BlockedMovementTickCount == 1,
            "default SampleScene collision must be observable on the authority");
    }

    private static GameWorld CreateWorldWithWall(Aabb2D wall, Vec2D tankHalfExtents)
    {
        TankCollisionMap2D map = new TankCollisionMap2D(
            new Aabb2D(Vec2D.Zero, new Vec2D(20f, 20f)),
            new[] { StaticCollider2D.FromAabb(1, wall) });
        TankCollisionSettings2D collisionSettings = new TankCollisionSettings2D(
            tankHalfExtents,
            0.05f,
            0.05f,
            128,
            8);
        TankWorldCollision2D resolver = new TankWorldCollision2D(map, collisionSettings);
        return new GameWorld(
            new GameWorldSettings
            {
                PlayerMoveSpeed = 7f,
                PlayerTurnDegreesPerSecond = 180f,
                InputHoldTimeoutTicks = 6
            },
            resolver);
    }
}
