using CalabiYau.CollisionCore;
using CalabiYau.TankCollision;

internal static class GameWorldGameplayQueryTests
{
    public static void RunAll()
    {
        StaticMapQueriesReturnNearestRayAndOverlapCollider();
        StaticWallBlocksAPlayerBehindIt();
        PlayerBeforeStaticWallKeepsTheHit();
        LagCompensatedTargetCannotBeHitThroughAStaticWall();
        InitialSpawnSkipsStaticInvalidCandidate();
        InitialSpawnSkipsAlivePlayerOccupancy();
        MissingInitialSpawnReturnsAnExplicitFailure();
        RespawnSkipsAnOccupiedPreferredCandidate();
    }

    private static void StaticMapQueriesReturnNearestRayAndOverlapCollider()
    {
        TankCollisionMap2D map = new TankCollisionMap2D(
            new Aabb2D(Vec2D.Zero, new Vec2D(20f, 20f)),
            new[]
            {
                StaticCollider2D.FromAabb(
                    20,
                    new Aabb2D(new Vec2D(6f, 0f), new Vec2D(0.5f, 2f))),
                StaticCollider2D.FromGameplayYaw(
                    10,
                    new Vec2D(3f, 0f),
                    new Vec2D(0.5f, 2f),
                    DegreesToRadians(20f))
            });
        TankMapQueries2D queries = new TankMapQueries2D(map);

        CollisionCoreTests.Assert(
            queries.RaycastStatic(
                new Ray2D(Vec2D.Zero, Vec2D.UnitX),
                10f,
                out StaticRaycastHit2D rayHit),
            "static map ray must hit the nearest collider");
        CollisionCoreTests.Assert(rayHit.ColliderId == 10, "nearest static collider id must be returned");
        CollisionCoreTests.Assert(rayHit.Distance > 2f && rayHit.Distance < 3f, "rotated OBB ray entry distance must be finite and nearest");

        CollisionCoreTests.Assert(
            queries.OverlapStatic(
                new Circle2D(new Vec2D(6f, 0f), 0.75f),
                out StaticOverlapHit2D circleHit),
            "circle gameplay query must overlap a static collider");
        CollisionCoreTests.Assert(circleHit.ColliderId == 20, "circle overlap must identify its static collider");

        CollisionCoreTests.Assert(
            queries.OverlapStatic(
                new Obb2D(new Vec2D(3f, 0f), new Vec2D(0.25f, 0.25f), 0f),
                out StaticOverlapHit2D obbHit),
            "OBB gameplay query must overlap a static collider");
        CollisionCoreTests.Assert(obbHit.ColliderId == 10, "OBB overlap must identify its static collider");
        CollisionCoreTests.Assert(
            !queries.IsInsideWorldBounds(
                new Circle2D(new Vec2D(19.75f, 0f), 0.5f)),
            "gameplay query must reject a shape extending outside world bounds");

        TankMapQueries2D overlappingQueries = new TankMapQueries2D(new TankCollisionMap2D(
            new Aabb2D(Vec2D.Zero, new Vec2D(20f, 20f)),
            new[]
            {
                StaticCollider2D.FromAabb(
                    1,
                    new Aabb2D(Vec2D.Zero, new Vec2D(1f, 1f))),
                StaticCollider2D.FromAabb(
                    2,
                    new Aabb2D(Vec2D.Zero, new Vec2D(2f, 2f)))
            }));
        CollisionCoreTests.Assert(
            overlappingQueries.OverlapStatic(
                new Circle2D(new Vec2D(1.5f, 0f), 0.5f),
                out StaticOverlapHit2D deepestOverlap),
            "overlap query must detect contact plus penetration");
        CollisionCoreTests.Assert(
            deepestOverlap.ColliderId == 2
                && deepestOverlap.PenetrationDepth > CollisionMath2D.DefaultEpsilon,
            "overlap query must not let an earlier zero-depth contact hide a later penetration");
    }

    private static void StaticWallBlocksAPlayerBehindIt()
    {
        GameWorld world = CreateFireWorldWithVerticalWall(2.2f);
        AddShooterAndTarget(world, 0f, 6f);

        CollisionCoreTests.Assert(
            world.TryQueueFire(1, Fire(1, 6f, 0f)).IsAccepted,
            "wall-occlusion fire must queue");
        List<GameWorldEvent> events = world.Tick(1f / 30f);

        PlayerState target = world.Players.Single(player => player.PlayerId == 2);
        CollisionCoreTests.Assert(target.Health == world.MaxHealth, "target behind a nearer wall must not take damage");
        CollisionCoreTests.Assert(!events.OfType<HitResolvedEvent>().Any(), "wall-blocked fire must not emit a hit event");
        CollisionCoreTests.Assert(
            events.OfType<FireResultWorldEvent>().Single().Result == "fired-no-hit",
            "wall-blocked fire remains an authoritative no-hit result");
        FireResolvedEvent fireEvent = events.OfType<FireResolvedEvent>().Single();
        CollisionCoreTests.AssertNear(fireEvent.Range, 0.7f, "presentation ray must stop at static wall", 0.001f);
        CollisionCoreTests.Assert(world.StaticOccludedFireCount == 1, "static wall occlusion must be observable");
    }

    private static void PlayerBeforeStaticWallKeepsTheHit()
    {
        GameWorld world = CreateFireWorldWithVerticalWall(6f);
        AddShooterAndTarget(world, 0f, 4f);

        CollisionCoreTests.Assert(
            world.TryQueueFire(1, Fire(1, 4f, 0f)).IsAccepted,
            "unoccluded fire must queue");
        List<GameWorldEvent> events = world.Tick(1f / 30f);

        PlayerState target = world.Players.Single(player => player.PlayerId == 2);
        CollisionCoreTests.Assert(target.Health < world.MaxHealth, "target before the wall must still take damage");
        CollisionCoreTests.Assert(events.OfType<HitResolvedEvent>().Count() == 1, "unoccluded target must emit one hit event");
        CollisionCoreTests.Assert(
            events.OfType<FireResultWorldEvent>().Single().Result == "fired-hit",
            "target before wall must retain fired-hit result");
        CollisionCoreTests.Assert(world.StaticOccludedFireCount == 0, "farther wall must not win hit ordering");
    }

    private static void LagCompensatedTargetCannotBeHitThroughAStaticWall()
    {
        GameWorld world = CreateFireWorldWithVerticalWall(2.2f);
        AddShooterAndTarget(world, 0f, 6f);
        world.Tick(1f / 30f);

        PlayerState target = world.Players.Single(player => player.PlayerId == 2);
        target.X = 12f;

        FireCommand delayedFire = new FireCommand(
            1,
            1,
            6f,
            0f,
            1f / 30f,
            0f);
        CollisionCoreTests.Assert(world.TryQueueFire(1, delayedFire).IsAccepted, "lag-compensated fire must queue");
        List<GameWorldEvent> events = world.Tick(1f / 30f);

        FireResolvedEvent fireEvent = events.OfType<FireResolvedEvent>().Single();
        CollisionCoreTests.Assert(fireEvent.LagCompensated, "test fire must actually rewind player history");
        CollisionCoreTests.Assert(!events.OfType<HitResolvedEvent>().Any(), "static wall must still block rewound target history");
        CollisionCoreTests.Assert(target.Health == world.MaxHealth, "rewound target behind static wall must keep health");
        CollisionCoreTests.Assert(world.StaticOccludedFireCount == 1, "lag-compensated occlusion must be counted");
    }

    private static void InitialSpawnSkipsStaticInvalidCandidate()
    {
        TankWorldCollision2D resolver = CreateResolver(new[]
        {
            StaticCollider2D.FromAabb(
                1,
                new Aabb2D(new Vec2D(-1f, 0.92f), new Vec2D(0.25f, 0.25f)))
        });
        GameWorld world = new GameWorld(new GameWorldSettings(), resolver);

        CollisionCoreTests.Assert(world.AddPlayer(1, out string reason), $"fallback spawn must succeed: {reason}");
        PlayerState player = world.Players.Single();
        CollisionCoreTests.AssertNear(player.X, 4f, "static-invalid preferred spawn must fall back to next candidate");
        CollisionCoreTests.Assert(
            world.SpawnRejectionsByReason.ContainsKey("overlaps-static-collider"),
            "static-invalid spawn reason must be observable");
    }

    private static void InitialSpawnSkipsAlivePlayerOccupancy()
    {
        GameWorld world = new GameWorld();
        CollisionCoreTests.Assert(world.AddPlayer(1), "occupancy test player 1 must be added");
        PlayerState first = world.Players.Single();
        first.X = 4f;
        first.Z = 0f;

        CollisionCoreTests.Assert(world.AddPlayer(2, out string reason), $"occupied spawn fallback must succeed: {reason}");
        PlayerState second = world.Players.Single(player => player.PlayerId == 2);
        CollisionCoreTests.AssertNear(second.X, 8f, "occupied preferred spawn must fall back deterministically");
        CollisionCoreTests.Assert(
            world.SpawnRejectionsByReason.ContainsKey("occupied-by-alive-player"),
            "alive-player spawn occupancy must be observable");
    }

    private static void MissingInitialSpawnReturnsAnExplicitFailure()
    {
        TankWorldCollision2D resolver = CreateResolver(new[]
        {
            StaticCollider2D.FromAabb(
                1,
                new Aabb2D(Vec2D.Zero, new Vec2D(25f, 25f)))
        });
        GameWorld world = new GameWorld(new GameWorldSettings(), resolver);

        CollisionCoreTests.Assert(
            !world.AddPlayer(1, out string reason),
            "fully blocked map must reject initial spawn");
        CollisionCoreTests.Assert(reason == "no-valid-spawn-candidate", "spawn failure must return a stable explicit reason");
        CollisionCoreTests.Assert(world.Players.Count == 0, "failed spawn must not insert a player into the world");
        CollisionCoreTests.Assert(world.SpawnPlacementFailureCount == 1, "failed spawn placement must be observable");
    }

    private static void RespawnSkipsAnOccupiedPreferredCandidate()
    {
        GameWorld world = new GameWorld(new GameWorldSettings
        {
            FireDamage = 100,
            FireCooldownSeconds = 0f,
            RespawnDelaySeconds = 1f / 30f,
            AimToleranceMeters = 50f
        });
        CollisionCoreTests.Assert(world.AddPlayer(1), "respawn test player 1 must be added");
        CollisionCoreTests.Assert(world.AddPlayer(2), "respawn test player 2 must be added");
        CollisionCoreTests.Assert(world.TryQueueFire(1, Fire(1, 4f, 0f)).IsAccepted, "killing fire must queue");
        world.Tick(1f / 30f);

        PlayerState first = world.Players.Single(player => player.PlayerId == 1);
        PlayerState second = world.Players.Single(player => player.PlayerId == 2);
        CollisionCoreTests.Assert(!second.IsAlive, "target must be dead before respawn selection");
        first.X = 4f;
        first.Z = 0f;

        List<GameWorldEvent> respawnEvents = world.Tick(1f / 30f);
        RespawnWorldEvent respawn = respawnEvents.OfType<RespawnWorldEvent>().Single();

        CollisionCoreTests.Assert(second.IsAlive, "player must respawn at the fallback candidate");
        CollisionCoreTests.AssertNear(respawn.X, 8f, "respawn must skip occupied preferred candidate");
        CollisionCoreTests.Assert(
            world.SpawnRejectionsByReason.ContainsKey("occupied-by-alive-player"),
            "respawn occupancy rejection must be observable");
    }

    private static void AddShooterAndTarget(GameWorld world, float shooterX, float targetX)
    {
        CollisionCoreTests.Assert(world.AddPlayer(1), "fire-query shooter must be added");
        CollisionCoreTests.Assert(world.AddPlayer(2), "fire-query target must be added");
        PlayerState shooter = world.Players.Single(player => player.PlayerId == 1);
        PlayerState target = world.Players.Single(player => player.PlayerId == 2);
        shooter.X = shooterX;
        shooter.Z = 0f;
        target.X = targetX;
        target.Z = 0f;
    }

    private static GameWorld CreateFireWorldWithVerticalWall(float wallX)
    {
        TankWorldCollision2D resolver = CreateResolver(new[]
        {
            StaticCollider2D.FromAabb(
                1,
                new Aabb2D(new Vec2D(wallX, 0f), new Vec2D(0.1f, 5f)))
        });
        return new GameWorld(new GameWorldSettings
        {
            FireCooldownSeconds = 0f,
            AimToleranceMeters = 50f
        }, resolver);
    }

    private static TankWorldCollision2D CreateResolver(StaticCollider2D[] colliders)
    {
        return new TankWorldCollision2D(
            new TankCollisionMap2D(
                new Aabb2D(Vec2D.Zero, new Vec2D(25f, 25f)),
                colliders),
            TrainingCollisionMap2D.CreateTankSettings());
    }

    private static FireCommand Fire(int sequence, float aimX, float aimZ)
    {
        return new FireCommand(sequence, 1, aimX, aimZ, 0f, 0f);
    }

    private static float DegreesToRadians(float degrees)
    {
        return degrees * MathF.PI / 180f;
    }
}
