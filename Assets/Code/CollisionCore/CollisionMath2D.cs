using System;

namespace CalabiYau.CollisionCore
{
    /// <summary>
    /// Minimal two-dimensional value type shared by Unity and the standalone server.
    /// In the current Tank game, X maps to world X and Y maps to world Z.
    /// </summary>
    public readonly struct Vec2D
    {
        public Vec2D(float x, float y)
        {
            if (!CollisionMath2D.IsFinite(x) || !CollisionMath2D.IsFinite(y))
            {
                throw new ArgumentException("Vec2D components must be finite.");
            }

            X = x;
            Y = y;
        }

        public float X { get; }
        public float Y { get; }

        public static Vec2D Zero => new Vec2D(0f, 0f);
        public static Vec2D UnitX => new Vec2D(1f, 0f);
        public static Vec2D UnitY => new Vec2D(0f, 1f);

        public float LengthSquared => X * X + Y * Y;
        public float Length => (float)Math.Sqrt(LengthSquared);
        public Vec2D PerpendicularLeft => new Vec2D(-Y, X);

        public static Vec2D operator +(Vec2D left, Vec2D right)
        {
            return new Vec2D(left.X + right.X, left.Y + right.Y);
        }

        public static Vec2D operator -(Vec2D left, Vec2D right)
        {
            return new Vec2D(left.X - right.X, left.Y - right.Y);
        }

        public static Vec2D operator -(Vec2D value)
        {
            return new Vec2D(-value.X, -value.Y);
        }

        public static Vec2D operator *(Vec2D value, float scale)
        {
            return new Vec2D(value.X * scale, value.Y * scale);
        }

        public static Vec2D operator *(float scale, Vec2D value)
        {
            return value * scale;
        }

        public static float Dot(Vec2D left, Vec2D right)
        {
            return left.X * right.X + left.Y * right.Y;
        }

        public bool TryNormalize(out Vec2D normalized, float epsilon = CollisionMath2D.DefaultEpsilon)
        {
            CollisionMath2D.ValidateEpsilon(epsilon);

            float lengthSquared = LengthSquared;

            if (lengthSquared <= epsilon * epsilon)
            {
                normalized = Zero;
                return false;
            }

            float inverseLength = 1f / (float)Math.Sqrt(lengthSquared);
            normalized = new Vec2D(X * inverseLength, Y * inverseLength);
            return true;
        }
    }

    public static class CollisionMath2D
    {
        // Numeric tolerance only. Gameplay skin width belongs to the later movement layer.
        public const float DefaultEpsilon = 0.00001f;

        public static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        public static bool TryProjectVector(
            Vec2D vector,
            Vec2D axis,
            out Vec2D projection,
            float epsilon = DefaultEpsilon)
        {
            if (!axis.TryNormalize(out Vec2D normalizedAxis, epsilon))
            {
                projection = Vec2D.Zero;
                return false;
            }

            projection = normalizedAxis * Vec2D.Dot(vector, normalizedAxis);
            return true;
        }

        public static float Clamp(float value, float minimum, float maximum)
        {
            if (value < minimum)
            {
                return minimum;
            }

            return value > maximum ? maximum : value;
        }

        internal static void ValidateEpsilon(float epsilon)
        {
            if (!IsFinite(epsilon) || epsilon <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(epsilon), "Epsilon must be finite and positive.");
            }
        }
    }
}
