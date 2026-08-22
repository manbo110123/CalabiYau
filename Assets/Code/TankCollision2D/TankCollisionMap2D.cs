using System;
using System.Collections.Generic;
using CalabiYau.CollisionCore;

namespace CalabiYau.TankCollision
{
    public readonly struct StaticCollider2D
    {
        public StaticCollider2D(int colliderId, Obb2D shape)
        {
            if (colliderId <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(colliderId), "Collider ID must be positive.");
            }

            if (shape.HalfExtents.X <= 0f || shape.HalfExtents.Y <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(shape), "Static collider half extents must be positive.");
            }

            ColliderId = colliderId;
            Shape = shape;
            BroadPhaseBounds = CollisionQueries2D.GetBoundingAabb(shape);
        }

        public int ColliderId { get; }
        public Obb2D Shape { get; }
        public Aabb2D BroadPhaseBounds { get; }

        public static StaticCollider2D FromAabb(int colliderId, Aabb2D shape)
        {
            return new StaticCollider2D(
                colliderId,
                new Obb2D(shape.Center, shape.HalfExtents, 0f));
        }

        // GameWorld/Unity yaw is clockwise on the X/Z plane. CollisionCore uses the
        // mathematical counter-clockwise convention on X/Y, so the sign is inverted.
        public static StaticCollider2D FromGameplayYaw(
            int colliderId,
            Vec2D center,
            Vec2D halfExtents,
            float gameplayYawRadians)
        {
            if (!CollisionMath2D.IsFinite(gameplayYawRadians))
            {
                throw new ArgumentException("Gameplay yaw must be finite.", nameof(gameplayYawRadians));
            }

            return new StaticCollider2D(
                colliderId,
                new Obb2D(center, halfExtents, -gameplayYawRadians));
        }
    }

    public sealed class TankCollisionMap2D
    {
        private readonly IReadOnlyList<StaticCollider2D> staticColliders;

        public TankCollisionMap2D(
            Aabb2D worldBounds,
            IEnumerable<StaticCollider2D> staticColliders,
            float epsilon = CollisionMath2D.DefaultEpsilon)
        {
            if (worldBounds.HalfExtents.X <= 0f || worldBounds.HalfExtents.Y <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(worldBounds), "World bounds half extents must be positive.");
            }

            if (!CollisionMath2D.IsFinite(epsilon) || epsilon <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(epsilon), "Map epsilon must be finite and positive.");
            }

            if (staticColliders == null)
            {
                throw new ArgumentNullException(nameof(staticColliders));
            }

            WorldBounds = worldBounds;
            Epsilon = epsilon;
            List<StaticCollider2D> validatedColliders = new List<StaticCollider2D>();
            HashSet<int> colliderIds = new HashSet<int>();

            foreach (StaticCollider2D collider in staticColliders)
            {
                if (!colliderIds.Add(collider.ColliderId))
                {
                    throw new ArgumentException(
                        $"Duplicate static collider ID {collider.ColliderId}.",
                        nameof(staticColliders));
                }

                if (!Contains(WorldBounds, collider.BroadPhaseBounds, epsilon))
                {
                    throw new ArgumentException(
                        $"Static collider {collider.ColliderId} is outside world bounds.",
                        nameof(staticColliders));
                }

                validatedColliders.Add(collider);
            }

            this.staticColliders = validatedColliders.AsReadOnly();
        }

        public Aabb2D WorldBounds { get; }
        public float Epsilon { get; }
        public IReadOnlyList<StaticCollider2D> StaticColliders => staticColliders;

        private static bool Contains(Aabb2D outer, Aabb2D inner, float epsilon)
        {
            return inner.Minimum.X >= outer.Minimum.X - epsilon
                && inner.Maximum.X <= outer.Maximum.X + epsilon
                && inner.Minimum.Y >= outer.Minimum.Y - epsilon
                && inner.Maximum.Y <= outer.Maximum.Y + epsilon;
        }
    }
}
