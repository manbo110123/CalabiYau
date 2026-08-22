using System;
using CalabiYau.CollisionCore;

namespace CalabiYau.TankCollision
{
    public readonly struct StaticRaycastHit2D
    {
        internal StaticRaycastHit2D(int colliderId, RaycastResult2D queryResult)
        {
            ColliderId = colliderId;
            Point = queryResult.Point;
            Normal = queryResult.Normal;
            Distance = queryResult.Distance;
            StartedInside = queryResult.StartedInside;
        }

        public int ColliderId { get; }
        public Vec2D Point { get; }
        public Vec2D Normal { get; }
        public float Distance { get; }
        public bool StartedInside { get; }
    }

    public readonly struct StaticOverlapHit2D
    {
        internal StaticOverlapHit2D(int colliderId, OverlapResult2D queryResult)
        {
            ColliderId = colliderId;
            Normal = queryResult.Normal;
            PenetrationDepth = queryResult.PenetrationDepth;
        }

        public int ColliderId { get; }
        public Vec2D Normal { get; }
        public float PenetrationDepth { get; }
    }

    /// <summary>
    /// Read-only gameplay queries over the same immutable static map used by Tank movement.
    /// It deliberately has no Unity, networking, damage, spawn-policy, or skill dependency.
    /// </summary>
    public sealed class TankMapQueries2D
    {
        private readonly TankCollisionMap2D map;

        public TankMapQueries2D(TankCollisionMap2D map)
        {
            this.map = map ?? throw new ArgumentNullException(nameof(map));
        }

        public TankCollisionMap2D Map => map;

        public bool RaycastStatic(
            Ray2D ray,
            float maximumDistance,
            out StaticRaycastHit2D nearestHit)
        {
            bool hasHit = false;
            int nearestColliderId = 0;
            RaycastResult2D nearestQuery = RaycastResult2D.NoHit;

            for (int index = 0; index < map.StaticColliders.Count; index++)
            {
                StaticCollider2D collider = map.StaticColliders[index];
                RaycastResult2D query = CollisionQueries2D.Raycast(
                    ray,
                    collider.Shape,
                    maximumDistance,
                    map.Epsilon);

                if (!query.Hit
                    || (hasHit
                        && query.Distance > nearestQuery.Distance + map.Epsilon)
                    || (hasHit
                        && Math.Abs(query.Distance - nearestQuery.Distance) <= map.Epsilon
                        && collider.ColliderId >= nearestColliderId))
                {
                    continue;
                }

                hasHit = true;
                nearestColliderId = collider.ColliderId;
                nearestQuery = query;
            }

            nearestHit = hasHit
                ? new StaticRaycastHit2D(nearestColliderId, nearestQuery)
                : default;
            return hasHit;
        }

        public bool OverlapStatic(Obb2D shape, out StaticOverlapHit2D deepestHit)
        {
            Aabb2D broadPhaseBounds = CollisionQueries2D.GetBoundingAabb(shape);
            bool hasHit = false;
            int deepestColliderId = 0;
            OverlapResult2D deepestQuery = OverlapResult2D.NoHit;

            for (int index = 0; index < map.StaticColliders.Count; index++)
            {
                StaticCollider2D collider = map.StaticColliders[index];

                if (!CollisionQueries2D.Overlap(
                        broadPhaseBounds,
                        collider.BroadPhaseBounds,
                        map.Epsilon).Hit)
                {
                    continue;
                }

                OverlapResult2D query = CollisionQueries2D.Overlap(
                    shape,
                    collider.Shape,
                    map.Epsilon);

                if (!query.Hit
                    || (hasHit
                        && query.PenetrationDepth < deepestQuery.PenetrationDepth - map.Epsilon)
                    || (hasHit
                        && Math.Abs(query.PenetrationDepth - deepestQuery.PenetrationDepth) <= map.Epsilon
                        && collider.ColliderId >= deepestColliderId))
                {
                    continue;
                }

                hasHit = true;
                deepestColliderId = collider.ColliderId;
                deepestQuery = query;
            }

            deepestHit = hasHit
                ? new StaticOverlapHit2D(deepestColliderId, deepestQuery)
                : default;
            return hasHit;
        }

        public bool OverlapStatic(Circle2D shape, out StaticOverlapHit2D deepestHit)
        {
            Aabb2D broadPhaseBounds = new Aabb2D(
                shape.Center,
                new Vec2D(shape.Radius, shape.Radius));
            bool hasHit = false;
            int deepestColliderId = 0;
            OverlapResult2D deepestQuery = OverlapResult2D.NoHit;

            for (int index = 0; index < map.StaticColliders.Count; index++)
            {
                StaticCollider2D collider = map.StaticColliders[index];

                if (!CollisionQueries2D.Overlap(
                        broadPhaseBounds,
                        collider.BroadPhaseBounds,
                        map.Epsilon).Hit)
                {
                    continue;
                }

                OverlapResult2D query = CollisionQueries2D.Overlap(
                    shape,
                    collider.Shape,
                    map.Epsilon);

                if (!query.Hit
                    || (hasHit
                        && query.PenetrationDepth < deepestQuery.PenetrationDepth - map.Epsilon)
                    || (hasHit
                        && Math.Abs(query.PenetrationDepth - deepestQuery.PenetrationDepth) <= map.Epsilon
                        && collider.ColliderId >= deepestColliderId))
                {
                    continue;
                }

                hasHit = true;
                deepestColliderId = collider.ColliderId;
                deepestQuery = query;
            }

            deepestHit = hasHit
                ? new StaticOverlapHit2D(deepestColliderId, deepestQuery)
                : default;
            return hasHit;
        }

        public bool IsInsideWorldBounds(Obb2D shape)
        {
            Aabb2D bounds = CollisionQueries2D.GetBoundingAabb(shape);
            return bounds.Minimum.X >= map.WorldBounds.Minimum.X - map.Epsilon
                && bounds.Maximum.X <= map.WorldBounds.Maximum.X + map.Epsilon
                && bounds.Minimum.Y >= map.WorldBounds.Minimum.Y - map.Epsilon
                && bounds.Maximum.Y <= map.WorldBounds.Maximum.Y + map.Epsilon;
        }

        public bool IsInsideWorldBounds(Circle2D shape)
        {
            return shape.Center.X - shape.Radius >= map.WorldBounds.Minimum.X - map.Epsilon
                && shape.Center.X + shape.Radius <= map.WorldBounds.Maximum.X + map.Epsilon
                && shape.Center.Y - shape.Radius >= map.WorldBounds.Minimum.Y - map.Epsilon
                && shape.Center.Y + shape.Radius <= map.WorldBounds.Maximum.Y + map.Epsilon;
        }
    }
}
