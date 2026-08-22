using System;

namespace CalabiYau.CollisionCore
{
    public readonly struct ProjectionInterval2D
    {
        public ProjectionInterval2D(float minimum, float maximum)
        {
            if (!CollisionMath2D.IsFinite(minimum)
                || !CollisionMath2D.IsFinite(maximum)
                || minimum > maximum)
            {
                throw new ArgumentException("Projection interval must be finite and ordered.");
            }

            Minimum = minimum;
            Maximum = maximum;
        }

        public float Minimum { get; }
        public float Maximum { get; }
    }

    public readonly struct OverlapResult2D
    {
        private OverlapResult2D(bool hit, Vec2D normal, float penetrationDepth)
        {
            Hit = hit;
            Normal = normal;
            PenetrationDepth = penetrationDepth;
        }

        public bool Hit { get; }
        public Vec2D Normal { get; }
        public float PenetrationDepth { get; }

        public static OverlapResult2D NoHit => new OverlapResult2D(false, Vec2D.Zero, 0f);

        internal static OverlapResult2D CreateHit(Vec2D normal, float penetrationDepth)
        {
            return new OverlapResult2D(true, normal, Math.Max(0f, penetrationDepth));
        }

        internal OverlapResult2D Reversed()
        {
            return Hit ? CreateHit(-Normal, PenetrationDepth) : this;
        }
    }

    public readonly struct RaycastResult2D
    {
        private RaycastResult2D(
            bool hit,
            Vec2D point,
            Vec2D normal,
            float distance,
            bool startedInside)
        {
            Hit = hit;
            Point = point;
            Normal = normal;
            Distance = distance;
            StartedInside = startedInside;
        }

        public bool Hit { get; }
        public Vec2D Point { get; }
        public Vec2D Normal { get; }
        public float Distance { get; }
        public bool StartedInside { get; }

        public static RaycastResult2D NoHit => new RaycastResult2D(false, Vec2D.Zero, Vec2D.Zero, 0f, false);

        internal static RaycastResult2D CreateHit(
            Vec2D point,
            Vec2D normal,
            float distance,
            bool startedInside)
        {
            return new RaycastResult2D(true, point, normal, Math.Max(0f, distance), startedInside);
        }
    }
}
