using System;
using CalabiYau.CollisionCore;

namespace CalabiYau.TankCollision
{
    public readonly struct TankPose2D
    {
        public TankPose2D(Vec2D position, float gameplayYawRadians)
        {
            if (!CollisionMath2D.IsFinite(gameplayYawRadians))
            {
                throw new ArgumentException("Gameplay yaw must be finite.", nameof(gameplayYawRadians));
            }

            Position = position;
            GameplayYawRadians = gameplayYawRadians;
        }

        public Vec2D Position { get; }
        public float GameplayYawRadians { get; }
    }

    public sealed class TankCollisionSettings2D
    {
        public TankCollisionSettings2D(
            Vec2D tankHalfExtents,
            float skinWidth,
            float maxSubstepDistance,
            int maxMovementSubsteps,
            int maxCollisionIterations,
            float epsilon = CollisionMath2D.DefaultEpsilon,
            Vec2D? tankCenterOffset = null)
        {
            if (tankHalfExtents.X <= 0f || tankHalfExtents.Y <= 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(tankHalfExtents),
                    "Tank half extents must be positive.");
            }

            if (!CollisionMath2D.IsFinite(skinWidth) || skinWidth < 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(skinWidth),
                    "Skin width must be finite and non-negative.");
            }

            if (!CollisionMath2D.IsFinite(maxSubstepDistance) || maxSubstepDistance <= 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maxSubstepDistance),
                    "Maximum substep distance must be finite and positive.");
            }

            if (maxMovementSubsteps <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maxMovementSubsteps),
                    "Maximum movement substeps must be positive.");
            }

            if (maxCollisionIterations <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maxCollisionIterations),
                    "Maximum collision iterations must be positive.");
            }

            if (!CollisionMath2D.IsFinite(epsilon) || epsilon <= 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(epsilon),
                    "Solver epsilon must be finite and positive.");
            }

            TankHalfExtents = tankHalfExtents;
            TankCenterOffset = tankCenterOffset ?? Vec2D.Zero;
            SkinWidth = skinWidth;
            MaxSubstepDistance = maxSubstepDistance;
            MaxMovementSubsteps = maxMovementSubsteps;
            MaxCollisionIterations = maxCollisionIterations;
            Epsilon = epsilon;
        }

        public Vec2D TankHalfExtents { get; }
        public Vec2D TankCenterOffset { get; }
        public float SkinWidth { get; }
        public float MaxSubstepDistance { get; }
        public int MaxMovementSubsteps { get; }
        public int MaxCollisionIterations { get; }
        public float Epsilon { get; }
        public Vec2D ExpandedTankHalfExtents => new Vec2D(
            TankHalfExtents.X + SkinWidth,
            TankHalfExtents.Y + SkinWidth);
    }

    public readonly struct TankMoveResult2D
    {
        internal TankMoveResult2D(
            TankPose2D pose,
            Vec2D requestedTranslation,
            Vec2D appliedTranslation,
            bool rotationApplied,
            bool rotationBlocked,
            bool wasBlocked,
            int collisionCount,
            bool reachedSubstepLimit,
            bool reachedCollisionIterationLimit)
        {
            Pose = pose;
            RequestedTranslation = requestedTranslation;
            AppliedTranslation = appliedTranslation;
            RotationApplied = rotationApplied;
            RotationBlocked = rotationBlocked;
            WasBlocked = wasBlocked;
            CollisionCount = collisionCount;
            ReachedSubstepLimit = reachedSubstepLimit;
            ReachedCollisionIterationLimit = reachedCollisionIterationLimit;
        }

        public TankPose2D Pose { get; }
        public Vec2D RequestedTranslation { get; }
        public Vec2D AppliedTranslation { get; }
        public bool RotationApplied { get; }
        public bool RotationBlocked { get; }
        public bool WasBlocked { get; }
        public int CollisionCount { get; }
        public bool ReachedSubstepLimit { get; }
        public bool ReachedCollisionIterationLimit { get; }
    }

    /// <summary>
    /// Pure C# Tank movement against an immutable static two-dimensional map.
    /// X maps to Unity world X, and Y maps to Unity world Z.
    /// </summary>
    public sealed class TankWorldCollision2D
    {
        private readonly TankCollisionMap2D map;
        private readonly TankCollisionSettings2D settings;

        public TankWorldCollision2D(
            TankCollisionMap2D map,
            TankCollisionSettings2D settings)
        {
            this.map = map ?? throw new ArgumentNullException(nameof(map));
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        public TankCollisionMap2D Map => map;
        public TankCollisionSettings2D Settings => settings;

        public TankMoveResult2D Move(
            TankPose2D start,
            float forwardDistance,
            float desiredYawDeltaRadians)
        {
            if (!CollisionMath2D.IsFinite(forwardDistance))
            {
                throw new ArgumentException("Forward distance must be finite.", nameof(forwardDistance));
            }

            if (!CollisionMath2D.IsFinite(desiredYawDeltaRadians))
            {
                throw new ArgumentException(
                    "Desired yaw delta must be finite.",
                    nameof(desiredYawDeltaRadians));
            }

            if (!IsPoseValid(start))
            {
                throw new ArgumentException(
                    "The starting Tank pose penetrates the static collision map.",
                    nameof(start));
            }

            float desiredYaw = start.GameplayYawRadians + desiredYawDeltaRadians;

            if (!CollisionMath2D.IsFinite(desiredYaw))
            {
                throw new ArgumentException("The resulting gameplay yaw must be finite.");
            }

            bool hasRotationRequest = Math.Abs(desiredYawDeltaRadians) > settings.Epsilon;
            TankPose2D rotatedPose = new TankPose2D(start.Position, desiredYaw);
            bool rotationApplied = !hasRotationRequest || IsPoseValid(rotatedPose);
            bool rotationBlocked = hasRotationRequest && !rotationApplied;
            float resolvedYaw = rotationApplied ? desiredYaw : start.GameplayYawRadians;
            TankPose2D resolvedPose = new TankPose2D(start.Position, resolvedYaw);
            Vec2D forward = new Vec2D(
                (float)Math.Sin(resolvedYaw),
                (float)Math.Cos(resolvedYaw));
            Vec2D requestedTranslation = forward * forwardDistance;

            float absoluteDistance = Math.Abs(forwardDistance);
            float maximumProcessedDistance = settings.MaxSubstepDistance
                * settings.MaxMovementSubsteps;
            bool reachedSubstepLimit = absoluteDistance
                > maximumProcessedDistance + settings.Epsilon;
            float processedDistance = Math.Min(absoluteDistance, maximumProcessedDistance);
            int substepCount = processedDistance <= settings.Epsilon
                ? 0
                : Math.Min(
                    settings.MaxMovementSubsteps,
                    Math.Max(
                        1,
                        (int)Math.Ceiling(processedDistance / settings.MaxSubstepDistance)));
            float signedSubstepDistance = substepCount == 0
                ? 0f
                : Math.Sign(forwardDistance) * processedDistance / substepCount;

            Vec2D currentPosition = start.Position;
            bool wasBlocked = rotationBlocked || reachedSubstepLimit;
            int collisionCount = 0;
            bool reachedCollisionIterationLimit = false;

            for (int substepIndex = 0; substepIndex < substepCount; substepIndex++)
            {
                Vec2D previousPosition = currentPosition;
                Vec2D candidatePosition = previousPosition + forward * signedSubstepDistance;

                for (int iteration = 0;
                     iteration < settings.MaxCollisionIterations;
                     iteration++)
                {
                    Obb2D candidateShape = CreateTankShape(candidatePosition, resolvedYaw);

                    if (!TryFindDeepestPenetration(
                            candidateShape,
                            out Vec2D correctionNormal,
                            out float correctionDepth))
                    {
                        break;
                    }

                    candidatePosition += correctionNormal * (correctionDepth + settings.Epsilon);
                    wasBlocked = true;
                    collisionCount++;
                }

                TankPose2D candidatePose = new TankPose2D(candidatePosition, resolvedYaw);

                if (!IsPoseValid(candidatePose))
                {
                    // A pathological corner or invalid map topology must not leave the
                    // authoritative result penetrating. Keep the last valid substep.
                    reachedCollisionIterationLimit = true;
                    wasBlocked = true;
                    currentPosition = previousPosition;
                    break;
                }

                currentPosition = candidatePosition;
            }

            TankPose2D finalPose = new TankPose2D(currentPosition, resolvedYaw);
            return new TankMoveResult2D(
                finalPose,
                requestedTranslation,
                currentPosition - start.Position,
                rotationApplied,
                rotationBlocked,
                wasBlocked,
                collisionCount,
                reachedSubstepLimit,
                reachedCollisionIterationLimit);
        }

        public bool IsPoseValid(TankPose2D pose)
        {
            return !TryFindDeepestPenetration(
                CreateTankShape(pose.Position, pose.GameplayYawRadians),
                out _,
                out _);
        }

        public Obb2D CreateTankShape(TankPose2D pose)
        {
            return CreateTankShape(pose.Position, pose.GameplayYawRadians);
        }

        private Obb2D CreateTankShape(Vec2D position, float gameplayYawRadians)
        {
            // CollisionCore rotates counter-clockwise; gameplay/Unity yaw rotates
            // clockwise on X/Z. Local OBB Y therefore remains the Tank forward axis.
            Obb2D rootAlignedShape = new Obb2D(
                Vec2D.Zero,
                settings.ExpandedTankHalfExtents,
                -gameplayYawRadians);
            Vec2D rotatedCenterOffset = rootAlignedShape.AxisX * settings.TankCenterOffset.X
                + rootAlignedShape.AxisY * settings.TankCenterOffset.Y;
            return new Obb2D(
                position + rotatedCenterOffset,
                settings.ExpandedTankHalfExtents,
                -gameplayYawRadians);
        }

        private bool TryFindDeepestPenetration(
            Obb2D tankShape,
            out Vec2D correctionNormal,
            out float correctionDepth)
        {
            correctionNormal = Vec2D.Zero;
            correctionDepth = 0f;
            Aabb2D tankBounds = CollisionQueries2D.GetBoundingAabb(tankShape);
            Aabb2D worldBounds = map.WorldBounds;

            SelectDeeperCorrection(
                worldBounds.Minimum.X - tankBounds.Minimum.X,
                Vec2D.UnitX,
                ref correctionNormal,
                ref correctionDepth);
            SelectDeeperCorrection(
                tankBounds.Maximum.X - worldBounds.Maximum.X,
                -Vec2D.UnitX,
                ref correctionNormal,
                ref correctionDepth);
            SelectDeeperCorrection(
                worldBounds.Minimum.Y - tankBounds.Minimum.Y,
                Vec2D.UnitY,
                ref correctionNormal,
                ref correctionDepth);
            SelectDeeperCorrection(
                tankBounds.Maximum.Y - worldBounds.Maximum.Y,
                -Vec2D.UnitY,
                ref correctionNormal,
                ref correctionDepth);

            for (int index = 0; index < map.StaticColliders.Count; index++)
            {
                StaticCollider2D collider = map.StaticColliders[index];
                OverlapResult2D broadPhase = CollisionQueries2D.Overlap(
                    tankBounds,
                    collider.BroadPhaseBounds,
                    settings.Epsilon);

                if (!broadPhase.Hit || broadPhase.PenetrationDepth <= settings.Epsilon)
                {
                    continue;
                }

                OverlapResult2D narrowPhase = CollisionQueries2D.Overlap(
                    tankShape,
                    collider.Shape,
                    settings.Epsilon);

                if (!narrowPhase.Hit)
                {
                    continue;
                }

                SelectDeeperCorrection(
                    narrowPhase.PenetrationDepth,
                    narrowPhase.Normal,
                    ref correctionNormal,
                    ref correctionDepth);
            }

            return correctionDepth > settings.Epsilon;
        }

        private void SelectDeeperCorrection(
            float candidateDepth,
            Vec2D candidateNormal,
            ref Vec2D correctionNormal,
            ref float correctionDepth)
        {
            if (candidateDepth > correctionDepth + settings.Epsilon)
            {
                correctionNormal = candidateNormal;
                correctionDepth = candidateDepth;
            }
        }
    }
}
