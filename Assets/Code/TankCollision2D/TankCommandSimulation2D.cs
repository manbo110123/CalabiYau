using System;
using CalabiYau.CollisionCore;

namespace CalabiYau.TankCollision
{
    /// <summary>
    /// Converts a validated movement command into one collision-resolved simulation step.
    /// GameWorld, immediate client prediction, and reconciliation replay share this formula.
    /// </summary>
    public static class TankCommandSimulation2D
    {
        // Client prediction currently runs from Unity's 50 Hz FixedUpdate while the
        // authority and command replay run at 30 Hz. Collision/rotation decisions are
        // nonlinear, so one large outer step is not equivalent to several small ones.
        // Both callers therefore integrate through the same 150 Hz command microsteps.
        public const float MaximumSimulationSubstepSeconds = 1f / 150f;
        private const int MaximumSimulationSubsteps = 256;

        public static TankMoveResult2D Simulate(
            TankWorldCollision2D collisionWorld,
            TankPose2D start,
            float moveAxis,
            float turnAxis,
            float moveSpeed,
            float turnDegreesPerSecond,
            float deltaTime)
        {
            if (collisionWorld == null)
            {
                throw new ArgumentNullException(nameof(collisionWorld));
            }

            ValidateFinite(moveAxis, nameof(moveAxis));
            ValidateFinite(turnAxis, nameof(turnAxis));
            ValidateNonNegativeFinite(moveSpeed, nameof(moveSpeed));
            ValidateNonNegativeFinite(turnDegreesPerSecond, nameof(turnDegreesPerSecond));
            ValidateNonNegativeFinite(deltaTime, nameof(deltaTime));

            float clampedMoveAxis = CollisionMath2D.Clamp(moveAxis, -1f, 1f);
            float clampedTurnAxis = CollisionMath2D.Clamp(turnAxis, -1f, 1f);

            if (deltaTime <= collisionWorld.Settings.Epsilon)
            {
                return collisionWorld.Move(start, 0f, 0f);
            }

            double rawSubstepCount = deltaTime / MaximumSimulationSubstepSeconds;
            double roundedSubstepCount = Math.Round(rawSubstepCount);

            // 1/30 and 1/50 are intended to be exactly five and three 150 Hz steps.
            // Absorb insignificant float representation error instead of accidentally
            // choosing six steps on one side of the network boundary.
            if (Math.Abs(rawSubstepCount - roundedSubstepCount)
                <= 0.0001d * Math.Max(1d, rawSubstepCount))
            {
                rawSubstepCount = roundedSubstepCount;
            }

            bool reachedCommandSubstepLimit = rawSubstepCount > MaximumSimulationSubsteps;
            int substepCount = Math.Max(
                1,
                Math.Min(
                    MaximumSimulationSubsteps,
                    (int)Math.Ceiling(rawSubstepCount)));
            float substepDeltaTime = deltaTime / substepCount;
            float forwardDistancePerSubstep = clampedMoveAxis * moveSpeed * substepDeltaTime;
            float yawDeltaRadiansPerSubstep = clampedTurnAxis
                * turnDegreesPerSecond
                * substepDeltaTime
                * ((float)Math.PI / 180f);
            bool hasRotationRequest = Math.Abs(yawDeltaRadiansPerSubstep)
                > collisionWorld.Settings.Epsilon;
            bool anyRotationApplied = !hasRotationRequest;
            bool rotationBlocked = false;
            bool wasBlocked = reachedCommandSubstepLimit;
            int collisionCount = 0;
            bool reachedMovementSubstepLimit = reachedCommandSubstepLimit;
            bool reachedCollisionIterationLimit = false;
            Vec2D requestedTranslation = Vec2D.Zero;
            TankPose2D pose = start;

            for (int index = 0; index < substepCount; index++)
            {
                TankMoveResult2D movement = collisionWorld.Move(
                    pose,
                    forwardDistancePerSubstep,
                    yawDeltaRadiansPerSubstep);
                pose = movement.Pose;
                requestedTranslation += movement.RequestedTranslation;
                anyRotationApplied |= hasRotationRequest && movement.RotationApplied;
                rotationBlocked |= movement.RotationBlocked;
                wasBlocked |= movement.WasBlocked;
                collisionCount += movement.CollisionCount;
                reachedMovementSubstepLimit |= movement.ReachedSubstepLimit;
                reachedCollisionIterationLimit |= movement.ReachedCollisionIterationLimit;
            }

            return new TankMoveResult2D(
                pose,
                requestedTranslation,
                pose.Position - start.Position,
                anyRotationApplied,
                rotationBlocked,
                wasBlocked,
                collisionCount,
                reachedMovementSubstepLimit,
                reachedCollisionIterationLimit);
        }

        private static void ValidateFinite(float value, string parameterName)
        {
            if (!CollisionMath2D.IsFinite(value))
            {
                throw new ArgumentException("Movement command values must be finite.", parameterName);
            }
        }

        private static void ValidateNonNegativeFinite(float value, string parameterName)
        {
            if (!CollisionMath2D.IsFinite(value) || value < 0f)
            {
                throw new ArgumentOutOfRangeException(
                    parameterName,
                    "Movement timing and speed values must be finite and non-negative.");
            }
        }
    }
}
