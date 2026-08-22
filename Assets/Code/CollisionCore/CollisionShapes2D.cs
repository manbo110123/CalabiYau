using System;

namespace CalabiYau.CollisionCore
{
    public readonly struct Aabb2D
    {
        public Aabb2D(Vec2D center, Vec2D halfExtents)
        {
            ValidateHalfExtents(halfExtents);
            Center = center;
            HalfExtents = halfExtents;
        }

        public Vec2D Center { get; }
        public Vec2D HalfExtents { get; }
        public Vec2D Minimum => Center - HalfExtents;
        public Vec2D Maximum => Center + HalfExtents;

        internal static void ValidateHalfExtents(Vec2D halfExtents)
        {
            if (halfExtents.X < 0f || halfExtents.Y < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(halfExtents), "Half extents cannot be negative.");
            }
        }
    }

    public readonly struct Obb2D
    {
        public Obb2D(Vec2D center, Vec2D halfExtents, float rotationRadians)
        {
            Aabb2D.ValidateHalfExtents(halfExtents);

            if (!CollisionMath2D.IsFinite(rotationRadians))
            {
                throw new ArgumentException("OBB rotation must be finite.", nameof(rotationRadians));
            }

            float cosine = (float)Math.Cos(rotationRadians);
            float sine = (float)Math.Sin(rotationRadians);

            Center = center;
            HalfExtents = halfExtents;
            RotationRadians = rotationRadians;
            AxisX = new Vec2D(cosine, sine);
            AxisY = new Vec2D(-sine, cosine);
        }

        public Vec2D Center { get; }
        public Vec2D HalfExtents { get; }
        public float RotationRadians { get; }
        public Vec2D AxisX { get; }
        public Vec2D AxisY { get; }
    }

    public readonly struct Circle2D
    {
        public Circle2D(Vec2D center, float radius)
        {
            if (!CollisionMath2D.IsFinite(radius) || radius < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(radius), "Circle radius must be finite and non-negative.");
            }

            Center = center;
            Radius = radius;
        }

        public Vec2D Center { get; }
        public float Radius { get; }
    }

    public readonly struct Ray2D
    {
        public Ray2D(Vec2D origin, Vec2D direction, float epsilon = CollisionMath2D.DefaultEpsilon)
        {
            if (!direction.TryNormalize(out Vec2D normalizedDirection, epsilon))
            {
                throw new ArgumentException("Ray direction must not be near zero.", nameof(direction));
            }

            Origin = origin;
            Direction = normalizedDirection;
        }

        public Vec2D Origin { get; }
        public Vec2D Direction { get; }

        public static bool TryCreateFromPoints(
            Vec2D start,
            Vec2D end,
            out Ray2D ray,
            out float maximumDistance,
            float epsilon = CollisionMath2D.DefaultEpsilon)
        {
            Vec2D displacement = end - start;

            if (!displacement.TryNormalize(out Vec2D direction, epsilon))
            {
                ray = default;
                maximumDistance = 0f;
                return false;
            }

            maximumDistance = displacement.Length;
            ray = new Ray2D(start, direction, epsilon);
            return true;
        }
    }
}
