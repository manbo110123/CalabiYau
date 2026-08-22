using CalabiYau.CollisionCore;
using CalabiYau.TankCollision;

internal static class TankWorldCollision2DTests
{
    public static void RunAll()
    {
        RotatedObbBroadPhaseBoundsContainEveryCorner();
        CollisionMapValidatesIdsAndWorldContainment();
        GameplayYawMapsToTheCurrentTankForwardConvention();
        TankCenterOffsetRotatesWithGameplayYaw();
        SharedTrainingMapHasStableVersionAndValidSpawns();
        InclinedTrainingObstacleUsesTighterProjectedObb();
        SharedCommandSimulationMatchesThirtyAndFiftyHertzInOpenSpace();
        SharedCommandSimulationMatchesThirtyAndFiftyHertzAtObstacle();
        FrontCollisionStopsBeforeWall();
        DiagonalMovementSlidesAlongWall();
        RepeatedWallPushDoesNotDriftOrPenetrate();
        CornerResolutionStopsWithoutIterationLoop();
        RotationNearWallIsRejected();
        RotationInOpenSpaceIsAccepted();
        NarrowWorldAllowsFittingPoseButRejectsWideTurn();
        WorldBoundsBlockMovementAndAllowSliding();
        ThirtyHertzMovementCannotCrossThinWall();
        ExcessiveMovementStopsAtSubstepBudget();
    }

    private static void RotatedObbBroadPhaseBoundsContainEveryCorner()
    {
        Obb2D box = new Obb2D(Vec2D.Zero, new Vec2D(2f, 1f), DegreesToRadians(45f));
        Aabb2D bounds = CollisionQueries2D.GetBoundingAabb(box);
        float expectedExtent = 3f / (float)Math.Sqrt(2f);

        CollisionCoreTests.AssertNear(bounds.HalfExtents.X, expectedExtent, "rotated broad-phase X extent");
        CollisionCoreTests.AssertNear(bounds.HalfExtents.Y, expectedExtent, "rotated broad-phase Y extent");

        Vec2D[] corners =
        {
            box.Center + box.AxisX * box.HalfExtents.X + box.AxisY * box.HalfExtents.Y,
            box.Center + box.AxisX * box.HalfExtents.X - box.AxisY * box.HalfExtents.Y,
            box.Center - box.AxisX * box.HalfExtents.X + box.AxisY * box.HalfExtents.Y,
            box.Center - box.AxisX * box.HalfExtents.X - box.AxisY * box.HalfExtents.Y
        };

        for (int index = 0; index < corners.Length; index++)
        {
            CollisionCoreTests.Assert(
                corners[index].X >= bounds.Minimum.X - CollisionMath2D.DefaultEpsilon
                    && corners[index].X <= bounds.Maximum.X + CollisionMath2D.DefaultEpsilon
                    && corners[index].Y >= bounds.Minimum.Y - CollisionMath2D.DefaultEpsilon
                    && corners[index].Y <= bounds.Maximum.Y + CollisionMath2D.DefaultEpsilon,
                $"rotated OBB corner {index} must remain inside its broad-phase AABB");
        }
    }

    private static void CollisionMapValidatesIdsAndWorldContainment()
    {
        Aabb2D worldBounds = new Aabb2D(Vec2D.Zero, new Vec2D(5f, 5f));
        StaticCollider2D valid = StaticCollider2D.FromAabb(
            1,
            new Aabb2D(Vec2D.Zero, new Vec2D(1f, 1f)));
        TankCollisionMap2D map = new TankCollisionMap2D(worldBounds, new[] { valid });

        CollisionCoreTests.Assert(map.StaticColliders.Count == 1, "valid map must retain its static collider");

        ExpectThrows<ArgumentException>(
            () => new TankCollisionMap2D(worldBounds, new[] { valid, valid }),
            "duplicate collider ID");
        ExpectThrows<ArgumentException>(
            () => new TankCollisionMap2D(
                worldBounds,
                new[]
                {
                    StaticCollider2D.FromAabb(
                        2,
                        new Aabb2D(new Vec2D(4.5f, 0f), new Vec2D(1f, 1f)))
                }),
            "collider outside world bounds");
        ExpectThrows<ArgumentOutOfRangeException>(
            () => new StaticCollider2D(0, valid.Shape),
            "non-positive collider ID");
    }

    private static void GameplayYawMapsToTheCurrentTankForwardConvention()
    {
        StaticCollider2D collider = StaticCollider2D.FromGameplayYaw(
            1,
            Vec2D.Zero,
            new Vec2D(0.5f, 1f),
            DegreesToRadians(90f));

        // Local OBB Y is the Tank forward axis. At gameplay yaw +90 degrees, current
        // GameWorld movement points toward world +X.
        CollisionCoreTests.AssertVec(
            collider.Shape.AxisY,
            Vec2D.UnitX,
            "gameplay yaw +90 forward axis");
    }

    private static void TankCenterOffsetRotatesWithGameplayYaw()
    {
        TankCollisionMap2D map = new TankCollisionMap2D(
            new Aabb2D(Vec2D.Zero, new Vec2D(10f, 10f)),
            Array.Empty<StaticCollider2D>());
        TankCollisionSettings2D settings = new TankCollisionSettings2D(
            new Vec2D(0.5f, 1f),
            0f,
            0.05f,
            32,
            4,
            tankCenterOffset: new Vec2D(-1f, 0.92f));
        TankWorldCollision2D solver = new TankWorldCollision2D(map, settings);

        CollisionCoreTests.AssertVec(
            solver.CreateTankShape(new TankPose2D(Vec2D.Zero, 0f)).Center,
            new Vec2D(-1f, 0.92f),
            "yaw-zero Tank center offset");
        CollisionCoreTests.AssertVec(
            solver.CreateTankShape(new TankPose2D(Vec2D.Zero, DegreesToRadians(90f))).Center,
            new Vec2D(0.92f, 1f),
            "yaw-90 Tank center offset");
    }

    private static void SharedTrainingMapHasStableVersionAndValidSpawns()
    {
        CollisionCoreTests.Assert(
            TrainingCollisionMap2D.CollisionRevision == 4,
            "gameplay space queries must increment the shared collision revision");
        CollisionCoreTests.Assert(
            TrainingCollisionMap2D.IsCompatible(
                TrainingCollisionMap2D.MapId,
                TrainingCollisionMap2D.CollisionRevision),
            "the shared map must accept its own version");
        CollisionCoreTests.Assert(
            !TrainingCollisionMap2D.IsCompatible(
                TrainingCollisionMap2D.MapId,
                TrainingCollisionMap2D.CollisionRevision + 1),
            "the shared map must reject a different collision revision");

        TankWorldCollision2D solver = TrainingCollisionMap2D.CreateResolver();
        CollisionCoreTests.Assert(
            solver.Map.StaticColliders.Count == 3,
            "SampleScene training map must retain its three configured obstacles");
        CollisionCoreTests.Assert(
            solver.IsPoseValid(new TankPose2D(Vec2D.Zero, 0f)),
            "player-one spawn must be valid in the shared training map");
        CollisionCoreTests.Assert(
            solver.IsPoseValid(new TankPose2D(new Vec2D(4f, 0f), 0f)),
            "player-two spawn must be valid in the shared training map");

        for (int playerId = 1; playerId <= 33; playerId++)
        {
            Vec2D spawn = TrainingCollisionMap2D.GetSafeSpawnRootPosition(playerId);
            CollisionCoreTests.Assert(
                solver.IsPoseValid(new TankPose2D(spawn, 0f)),
                $"cycled spawn for player {playerId} must remain collision-valid");
        }
    }

    private static void InclinedTrainingObstacleUsesTighterProjectedObb()
    {
        StaticCollider2D collider = TrainingCollisionMap2D
            .CreateMap()
            .StaticColliders
            .Single(candidate => candidate.ColliderId == 2);

        CollisionCoreTests.Assert(
            Math.Abs(collider.Shape.RotationRadians) > 1f,
            "the inclined obstacle must no longer use an axis-aligned broad approximation");
        CollisionCoreTests.AssertNear(
            collider.Shape.HalfExtents.X,
            2.0163f,
            "inclined projected OBB X half extent",
            0.0001f);
        CollisionCoreTests.AssertNear(
            collider.Shape.HalfExtents.Y,
            1.5487f,
            "inclined projected OBB Y half extent",
            0.0001f);
    }

    private static void SharedCommandSimulationMatchesThirtyAndFiftyHertzInOpenSpace()
    {
        TankWorldCollision2D solver = CreateSolver(
            new Aabb2D(Vec2D.Zero, new Vec2D(10f, 10f)),
            Array.Empty<StaticCollider2D>());
        TankPose2D thirtyHertz = SimulateCommandAtRate(
            solver,
            new TankPose2D(Vec2D.Zero, 0f),
            30,
            1f,
            1f,
            1f,
            1f,
            90f,
            out _);
        TankPose2D fiftyHertz = SimulateCommandAtRate(
            solver,
            new TankPose2D(Vec2D.Zero, 0f),
            50,
            1f,
            1f,
            1f,
            1f,
            90f,
            out _);

        CollisionCoreTests.AssertNear(
            thirtyHertz.GameplayYawRadians,
            DegreesToRadians(90f),
            "30 Hz shared command yaw",
            0.0002f);
        CollisionCoreTests.AssertVec(
            thirtyHertz.Position,
            fiftyHertz.Position,
            "open-space shared command must match at 30 Hz and 50 Hz",
            0.0002f);
        CollisionCoreTests.AssertNear(
            thirtyHertz.GameplayYawRadians,
            fiftyHertz.GameplayYawRadians,
            "open-space yaw must match at 30 Hz and 50 Hz",
            0.0002f);
        CollisionCoreTests.Assert(
            thirtyHertz.Position.X > 0.6f && thirtyHertz.Position.Y > 0.6f,
            "microstepped turn-and-move must follow a smooth quarter arc");
    }

    private static void SharedCommandSimulationMatchesThirtyAndFiftyHertzAtObstacle()
    {
        TankWorldCollision2D solver = CreateSolver(
            new Aabb2D(Vec2D.Zero, new Vec2D(10f, 10f)),
            new[]
            {
                StaticCollider2D.FromAabb(
                    1,
                    new Aabb2D(Vec2D.Zero, new Vec2D(1.2f, 0.1f)))
            },
            new Vec2D(0.4f, 0.6f));
        TankPose2D start = new TankPose2D(new Vec2D(0f, -2f), 0f);
        TankPose2D thirtyHertz = SimulateCommandAtRate(
            solver,
            start,
            30,
            2f,
            1f,
            0.25f,
            2f,
            180f,
            out bool thirtyHertzBlocked);
        TankPose2D fiftyHertz = SimulateCommandAtRate(
            solver,
            start,
            50,
            2f,
            1f,
            0.25f,
            2f,
            180f,
            out bool fiftyHertzBlocked);

        CollisionCoreTests.Assert(
            thirtyHertzBlocked && fiftyHertzBlocked,
            "tick-rate comparison must actually contact the obstacle");
        CollisionCoreTests.AssertVec(
            thirtyHertz.Position,
            fiftyHertz.Position,
            "obstacle path must match at 30 Hz and 50 Hz",
            0.0005f);
        CollisionCoreTests.AssertNear(
            thirtyHertz.GameplayYawRadians,
            fiftyHertz.GameplayYawRadians,
            "obstacle yaw must match at 30 Hz and 50 Hz",
            0.0005f);
    }

    private static void FrontCollisionStopsBeforeWall()
    {
        TankWorldCollision2D solver = CreateSolver(
            new Aabb2D(Vec2D.Zero, new Vec2D(10f, 10f)),
            new[]
            {
                StaticCollider2D.FromAabb(
                    1,
                    new Aabb2D(new Vec2D(0f, 2f), new Vec2D(5f, 0.1f)))
            });
        TankMoveResult2D result = solver.Move(
            new TankPose2D(Vec2D.Zero, 0f),
            3f,
            0f);

        CollisionCoreTests.Assert(result.WasBlocked, "front collision must report blocking");
        CollisionCoreTests.Assert(result.CollisionCount > 0, "front collision must produce a contact");
        CollisionCoreTests.AssertNear(result.Pose.Position.X, 0f, "front collision X");
        CollisionCoreTests.AssertNear(result.Pose.Position.Y, 1.1f, "front collision stop position", 0.0002f);
        CollisionCoreTests.AssertNear(result.AppliedTranslation.Y, 1.1f, "front collision applied distance", 0.0002f);
        CollisionCoreTests.Assert(solver.IsPoseValid(result.Pose), "front collision result must remain valid");
        CollisionCoreTests.Assert(
            !result.ReachedCollisionIterationLimit,
            "simple front collision must not reach the iteration limit");
    }

    private static void DiagonalMovementSlidesAlongWall()
    {
        TankWorldCollision2D solver = CreateSolver(
            new Aabb2D(Vec2D.Zero, new Vec2D(10f, 10f)),
            new[]
            {
                StaticCollider2D.FromAabb(
                    1,
                    new Aabb2D(new Vec2D(2f, 0f), new Vec2D(0.1f, 6f)))
            });
        TankMoveResult2D result = solver.Move(
            new TankPose2D(new Vec2D(0f, -2f), DegreesToRadians(45f)),
            4f,
            0f);

        CollisionCoreTests.Assert(result.WasBlocked, "diagonal wall movement must report blocking");
        CollisionCoreTests.Assert(
            result.Pose.Position.X < 1f,
            "diagonal wall movement must stop on the wall normal");
        CollisionCoreTests.Assert(
            result.Pose.Position.Y > 0.7f,
            "diagonal wall movement must preserve substantial tangent travel");
        CollisionCoreTests.Assert(
            result.AppliedTranslation.Y > result.AppliedTranslation.X,
            "sliding must preserve more tangent than blocked normal movement");
        CollisionCoreTests.Assert(solver.IsPoseValid(result.Pose), "diagonal slide result must remain valid");
    }

    private static void RepeatedWallPushDoesNotDriftOrPenetrate()
    {
        TankWorldCollision2D solver = CreateSolver(
            new Aabb2D(Vec2D.Zero, new Vec2D(10f, 10f)),
            new[]
            {
                StaticCollider2D.FromAabb(
                    1,
                    new Aabb2D(new Vec2D(0f, 2f), new Vec2D(5f, 0.1f)))
            });
        TankPose2D pose = solver.Move(new TankPose2D(Vec2D.Zero, 0f), 3f, 0f).Pose;
        Vec2D stablePosition = pose.Position;

        for (int tick = 0; tick < 120; tick++)
        {
            TankMoveResult2D result = solver.Move(pose, 7f / 30f, 0f);
            pose = result.Pose;
            CollisionCoreTests.Assert(result.WasBlocked, $"wall push tick {tick} must remain blocked");
            CollisionCoreTests.Assert(
                solver.IsPoseValid(pose),
                $"wall push tick {tick} must not penetrate");
        }

        CollisionCoreTests.AssertVec(
            pose.Position,
            stablePosition,
            "repeated wall push must not drift",
            0.0002f);
    }

    private static void CornerResolutionStopsWithoutIterationLoop()
    {
        TankWorldCollision2D solver = CreateSolver(
            new Aabb2D(Vec2D.Zero, new Vec2D(10f, 10f)),
            new[]
            {
                StaticCollider2D.FromAabb(
                    1,
                    new Aabb2D(new Vec2D(2f, 0f), new Vec2D(0.1f, 6f))),
                StaticCollider2D.FromAabb(
                    2,
                    new Aabb2D(new Vec2D(0f, 2f), new Vec2D(6f, 0.1f)))
            });
        TankMoveResult2D result = solver.Move(
            new TankPose2D(Vec2D.Zero, DegreesToRadians(45f)),
            4f,
            0f);

        CollisionCoreTests.Assert(result.WasBlocked, "corner movement must report blocking");
        CollisionCoreTests.Assert(result.Pose.Position.X < 1f, "corner must block X travel");
        CollisionCoreTests.Assert(result.Pose.Position.Y < 1f, "corner must block Y travel");
        CollisionCoreTests.Assert(
            !result.ReachedCollisionIterationLimit,
            "ordinary two-wall corner must resolve within the iteration budget");
        CollisionCoreTests.Assert(solver.IsPoseValid(result.Pose), "corner result must remain valid");
    }

    private static void RotationNearWallIsRejected()
    {
        TankWorldCollision2D solver = CreateSolver(
            new Aabb2D(Vec2D.Zero, new Vec2D(10f, 10f)),
            new[]
            {
                StaticCollider2D.FromAabb(
                    1,
                    new Aabb2D(new Vec2D(2f, 0f), new Vec2D(0.1f, 6f)))
            },
            new Vec2D(0.4f, 1.2f));
        TankPose2D start = new TankPose2D(new Vec2D(0.7f, 0f), 0f);
        TankMoveResult2D result = solver.Move(start, 0f, DegreesToRadians(90f));

        CollisionCoreTests.Assert(result.RotationBlocked, "near-wall wide rotation must be rejected");
        CollisionCoreTests.Assert(!result.RotationApplied, "rejected rotation must not be applied");
        CollisionCoreTests.AssertNear(
            result.Pose.GameplayYawRadians,
            start.GameplayYawRadians,
            "rejected rotation yaw");
        CollisionCoreTests.AssertVec(result.Pose.Position, start.Position, "rejected rotation position");
        CollisionCoreTests.Assert(solver.IsPoseValid(result.Pose), "rejected rotation result must remain valid");
    }

    private static void RotationInOpenSpaceIsAccepted()
    {
        TankWorldCollision2D solver = CreateSolver(
            new Aabb2D(Vec2D.Zero, new Vec2D(10f, 10f)),
            Array.Empty<StaticCollider2D>(),
            new Vec2D(0.4f, 1.2f));
        float desiredYaw = DegreesToRadians(90f);
        TankMoveResult2D result = solver.Move(
            new TankPose2D(Vec2D.Zero, 0f),
            0f,
            desiredYaw);

        CollisionCoreTests.Assert(result.RotationApplied, "open-space rotation must be accepted");
        CollisionCoreTests.Assert(!result.RotationBlocked, "open-space rotation must not report blocking");
        CollisionCoreTests.AssertNear(
            result.Pose.GameplayYawRadians,
            desiredYaw,
            "accepted rotation yaw");
    }

    private static void NarrowWorldAllowsFittingPoseButRejectsWideTurn()
    {
        TankWorldCollision2D solver = CreateSolver(
            new Aabb2D(Vec2D.Zero, new Vec2D(0.6f, 5f)),
            Array.Empty<StaticCollider2D>(),
            new Vec2D(0.4f, 1.2f));
        TankPose2D start = new TankPose2D(Vec2D.Zero, 0f);

        CollisionCoreTests.Assert(solver.IsPoseValid(start), "lengthwise narrow-world pose must fit");

        TankMoveResult2D result = solver.Move(start, 0f, DegreesToRadians(90f));
        CollisionCoreTests.Assert(
            result.RotationBlocked,
            "narrow world must reject a turn whose wide footprint crosses the boundary");
        CollisionCoreTests.AssertNear(
            result.Pose.GameplayYawRadians,
            0f,
            "narrow-world rejected yaw");
    }

    private static void WorldBoundsBlockMovementAndAllowSliding()
    {
        TankWorldCollision2D solver = CreateSolver(
            new Aabb2D(Vec2D.Zero, new Vec2D(2f, 6f)),
            Array.Empty<StaticCollider2D>());
        TankMoveResult2D result = solver.Move(
            new TankPose2D(new Vec2D(0f, -2f), DegreesToRadians(45f)),
            4f,
            0f);

        CollisionCoreTests.Assert(result.WasBlocked, "world boundary must report blocking");
        CollisionCoreTests.Assert(
            result.Pose.Position.X < 1.1f,
            "world boundary must stop normal travel");
        CollisionCoreTests.Assert(
            result.Pose.Position.Y > 0.7f,
            "world boundary must retain tangent travel");
        CollisionCoreTests.Assert(solver.IsPoseValid(result.Pose), "world-bound slide must remain valid");
    }

    private static void ThirtyHertzMovementCannotCrossThinWall()
    {
        TankWorldCollision2D solver = CreateSolver(
            new Aabb2D(Vec2D.Zero, new Vec2D(10f, 10f)),
            new[]
            {
                StaticCollider2D.FromAabb(
                    1,
                    new Aabb2D(new Vec2D(0f, 2f), new Vec2D(5f, 0.025f)))
            });
        TankPose2D pose = new TankPose2D(Vec2D.Zero, 0f);

        for (int tick = 0; tick < 60; tick++)
        {
            pose = solver.Move(pose, 7f / 30f, 0f).Pose;
            CollisionCoreTests.Assert(
                solver.IsPoseValid(pose),
                $"thin-wall 30 Hz tick {tick} must remain valid");
        }

        CollisionCoreTests.Assert(
            pose.Position.Y < 1.18f,
            "30 Hz movement must not tunnel through a 0.05-unit wall");
    }

    private static void ExcessiveMovementStopsAtSubstepBudget()
    {
        TankWorldCollision2D solver = CreateSolver(
            new Aabb2D(Vec2D.Zero, new Vec2D(100f, 100f)),
            Array.Empty<StaticCollider2D>(),
            new Vec2D(0.5f, 0.75f),
            maxSubstepDistance: 0.1f,
            maxMovementSubsteps: 8);
        TankMoveResult2D result = solver.Move(
            new TankPose2D(Vec2D.Zero, 0f),
            100f,
            0f);

        CollisionCoreTests.Assert(result.ReachedSubstepLimit, "large move must report substep budget exhaustion");
        CollisionCoreTests.Assert(result.WasBlocked, "substep budget exhaustion must report incomplete movement");
        CollisionCoreTests.AssertNear(
            result.Pose.Position.Y,
            0.8f,
            "substep budget must cap processed distance",
            0.0002f);
        CollisionCoreTests.AssertNear(
            result.RequestedTranslation.Y,
            100f,
            "result must preserve the original requested distance");
    }

    private static TankWorldCollision2D CreateSolver(
        Aabb2D worldBounds,
        StaticCollider2D[] colliders,
        Vec2D? tankHalfExtents = null,
        float maxSubstepDistance = 0.05f,
        int maxMovementSubsteps = 128,
        int maxCollisionIterations = 8)
    {
        TankCollisionMap2D map = new TankCollisionMap2D(worldBounds, colliders);
        TankCollisionSettings2D settings = new TankCollisionSettings2D(
            tankHalfExtents ?? new Vec2D(0.5f, 0.75f),
            0.05f,
            maxSubstepDistance,
            maxMovementSubsteps,
            maxCollisionIterations);
        return new TankWorldCollision2D(map, settings);
    }

    private static TankPose2D SimulateCommandAtRate(
        TankWorldCollision2D solver,
        TankPose2D start,
        int outerTickRate,
        float durationSeconds,
        float moveAxis,
        float turnAxis,
        float moveSpeed,
        float turnDegreesPerSecond,
        out bool wasBlocked)
    {
        int tickCount = (int)Math.Round(durationSeconds * outerTickRate);
        float deltaTime = 1f / outerTickRate;
        TankPose2D pose = start;
        wasBlocked = false;

        for (int tick = 0; tick < tickCount; tick++)
        {
            TankMoveResult2D movement = TankCommandSimulation2D.Simulate(
                solver,
                pose,
                moveAxis,
                turnAxis,
                moveSpeed,
                turnDegreesPerSecond,
                deltaTime);
            pose = movement.Pose;
            wasBlocked |= movement.WasBlocked;
        }

        return pose;
    }

    private static float DegreesToRadians(float degrees)
    {
        return degrees * (float)Math.PI / 180f;
    }

    private static void ExpectThrows<TException>(Action action, string description)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException($"{description}: expected {typeof(TException).Name}");
    }
}
