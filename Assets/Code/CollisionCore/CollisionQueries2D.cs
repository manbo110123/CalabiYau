using System;

namespace CalabiYau.CollisionCore
{
    public static class CollisionQueries2D
    {
        public static Aabb2D GetBoundingAabb(Obb2D box)
        {
            float extentX = box.HalfExtents.X * Math.Abs(box.AxisX.X)
                + box.HalfExtents.Y * Math.Abs(box.AxisY.X);
            float extentY = box.HalfExtents.X * Math.Abs(box.AxisX.Y)
                + box.HalfExtents.Y * Math.Abs(box.AxisY.Y);
            return new Aabb2D(box.Center, new Vec2D(extentX, extentY));
        }

        public static ProjectionInterval2D Project(
            Aabb2D box,
            Vec2D axis,
            float epsilon = CollisionMath2D.DefaultEpsilon)
        {
            Vec2D normalizedAxis = NormalizeAxis(axis, epsilon);
            float centerProjection = Vec2D.Dot(box.Center, normalizedAxis);
            float radius = box.HalfExtents.X * Math.Abs(normalizedAxis.X)
                + box.HalfExtents.Y * Math.Abs(normalizedAxis.Y);
            return new ProjectionInterval2D(centerProjection - radius, centerProjection + radius);
        }

        public static ProjectionInterval2D Project(
            Obb2D box,
            Vec2D axis,
            float epsilon = CollisionMath2D.DefaultEpsilon)
        {
            Vec2D normalizedAxis = NormalizeAxis(axis, epsilon);
            float centerProjection = Vec2D.Dot(box.Center, normalizedAxis);
            float radius = box.HalfExtents.X * Math.Abs(Vec2D.Dot(box.AxisX, normalizedAxis))
                + box.HalfExtents.Y * Math.Abs(Vec2D.Dot(box.AxisY, normalizedAxis));
            return new ProjectionInterval2D(centerProjection - radius, centerProjection + radius);
        }

        public static ProjectionInterval2D Project(
            Circle2D circle,
            Vec2D axis,
            float epsilon = CollisionMath2D.DefaultEpsilon)
        {
            Vec2D normalizedAxis = NormalizeAxis(axis, epsilon);
            float centerProjection = Vec2D.Dot(circle.Center, normalizedAxis);
            return new ProjectionInterval2D(
                centerProjection - circle.Radius,
                centerProjection + circle.Radius);
        }

        public static OverlapResult2D Overlap(
            Aabb2D first,
            Aabb2D second,
            float epsilon = CollisionMath2D.DefaultEpsilon)
        {
            CollisionMath2D.ValidateEpsilon(epsilon);

            if (!TryEvaluateAxis(
                    first.Center,
                    second.Center,
                    Project(first, Vec2D.UnitX, epsilon),
                    Project(second, Vec2D.UnitX, epsilon),
                    Vec2D.UnitX,
                    epsilon,
                    out Vec2D bestNormal,
                    out float bestDepth))
            {
                return OverlapResult2D.NoHit;
            }

            if (!TryEvaluateAxis(
                    first.Center,
                    second.Center,
                    Project(first, Vec2D.UnitY, epsilon),
                    Project(second, Vec2D.UnitY, epsilon),
                    Vec2D.UnitY,
                    epsilon,
                    out Vec2D candidateNormal,
                    out float candidateDepth))
            {
                return OverlapResult2D.NoHit;
            }

            SelectShallowerAxis(
                candidateNormal,
                candidateDepth,
                epsilon,
                ref bestNormal,
                ref bestDepth);
            return OverlapResult2D.CreateHit(bestNormal, bestDepth);
        }

        public static OverlapResult2D Overlap(
            Circle2D first,
            Aabb2D second,
            float epsilon = CollisionMath2D.DefaultEpsilon)
        {
            Obb2D box = new Obb2D(second.Center, second.HalfExtents, 0f);
            return Overlap(first, box, epsilon);
        }

        public static OverlapResult2D Overlap(
            Aabb2D first,
            Circle2D second,
            float epsilon = CollisionMath2D.DefaultEpsilon)
        {
            return Overlap(second, first, epsilon).Reversed();
        }

        public static OverlapResult2D Overlap(
            Circle2D first,
            Obb2D second,
            float epsilon = CollisionMath2D.DefaultEpsilon)
        {
            CollisionMath2D.ValidateEpsilon(epsilon);

            Vec2D centerOffset = first.Center - second.Center;
            float localX = Vec2D.Dot(centerOffset, second.AxisX);
            float localY = Vec2D.Dot(centerOffset, second.AxisY);
            float closestX = CollisionMath2D.Clamp(localX, -second.HalfExtents.X, second.HalfExtents.X);
            float closestY = CollisionMath2D.Clamp(localY, -second.HalfExtents.Y, second.HalfExtents.Y);
            float differenceX = localX - closestX;
            float differenceY = localY - closestY;
            float distanceSquared = differenceX * differenceX + differenceY * differenceY;
            float allowedDistance = first.Radius + epsilon;

            if (distanceSquared > allowedDistance * allowedDistance)
            {
                return OverlapResult2D.NoHit;
            }

            bool centerInside = Math.Abs(localX) <= second.HalfExtents.X
                && Math.Abs(localY) <= second.HalfExtents.Y;

            if (!centerInside)
            {
                float distance = (float)Math.Sqrt(distanceSquared);
                Vec2D normal;

                if (distance > epsilon)
                {
                    normal = second.AxisX * (differenceX / distance)
                        + second.AxisY * (differenceY / distance);
                }
                else
                {
                    normal = GetOutsideFallbackNormal(localX, localY, second);
                }

                return OverlapResult2D.CreateHit(normal, Math.Max(0f, first.Radius - distance));
            }

            GetNearestInsideFace(
                localX,
                localY,
                second,
                out Vec2D insideNormal,
                out float distanceToFace);
            return OverlapResult2D.CreateHit(insideNormal, first.Radius + distanceToFace);
        }

        public static OverlapResult2D Overlap(
            Obb2D first,
            Circle2D second,
            float epsilon = CollisionMath2D.DefaultEpsilon)
        {
            return Overlap(second, first, epsilon).Reversed();
        }

        public static OverlapResult2D Overlap(
            Obb2D first,
            Obb2D second,
            float epsilon = CollisionMath2D.DefaultEpsilon)
        {
            CollisionMath2D.ValidateEpsilon(epsilon);

            if (!TryEvaluateObbAxis(
                    first,
                    second,
                    first.AxisX,
                    epsilon,
                    out Vec2D bestNormal,
                    out float bestDepth))
            {
                return OverlapResult2D.NoHit;
            }

            if (!TryEvaluateObbAxis(
                    first,
                    second,
                    first.AxisY,
                    epsilon,
                    out Vec2D candidateNormal,
                    out float candidateDepth))
            {
                return OverlapResult2D.NoHit;
            }

            SelectShallowerAxis(
                candidateNormal,
                candidateDepth,
                epsilon,
                ref bestNormal,
                ref bestDepth);

            if (!TryEvaluateObbAxis(
                    first,
                    second,
                    second.AxisX,
                    epsilon,
                    out candidateNormal,
                    out candidateDepth))
            {
                return OverlapResult2D.NoHit;
            }

            SelectShallowerAxis(
                candidateNormal,
                candidateDepth,
                epsilon,
                ref bestNormal,
                ref bestDepth);

            if (!TryEvaluateObbAxis(
                    first,
                    second,
                    second.AxisY,
                    epsilon,
                    out candidateNormal,
                    out candidateDepth))
            {
                return OverlapResult2D.NoHit;
            }

            SelectShallowerAxis(
                candidateNormal,
                candidateDepth,
                epsilon,
                ref bestNormal,
                ref bestDepth);
            return OverlapResult2D.CreateHit(bestNormal, bestDepth);
        }

        public static OverlapResult2D Overlap(
            Aabb2D first,
            Obb2D second,
            float epsilon = CollisionMath2D.DefaultEpsilon)
        {
            return Overlap(
                new Obb2D(first.Center, first.HalfExtents, 0f),
                second,
                epsilon);
        }

        public static OverlapResult2D Overlap(
            Obb2D first,
            Aabb2D second,
            float epsilon = CollisionMath2D.DefaultEpsilon)
        {
            return Overlap(
                first,
                new Obb2D(second.Center, second.HalfExtents, 0f),
                epsilon);
        }

        public static bool AreSeparated(
            ProjectionInterval2D first,
            ProjectionInterval2D second,
            float epsilon = CollisionMath2D.DefaultEpsilon)
        {
            CollisionMath2D.ValidateEpsilon(epsilon);
            return first.Maximum < second.Minimum - epsilon
                || second.Maximum < first.Minimum - epsilon;
        }

        public static RaycastResult2D Raycast(
            Ray2D ray,
            Aabb2D box,
            float maximumDistance,
            float epsilon = CollisionMath2D.DefaultEpsilon)
        {
            return Raycast(
                ray,
                new Obb2D(box.Center, box.HalfExtents, 0f),
                maximumDistance,
                epsilon);
        }

        public static RaycastResult2D Raycast(
            Ray2D ray,
            Circle2D circle,
            float maximumDistance,
            float epsilon = CollisionMath2D.DefaultEpsilon)
        {
            ValidateMaximumDistance(maximumDistance);
            CollisionMath2D.ValidateEpsilon(epsilon);

            Vec2D originFromCenter = ray.Origin - circle.Center;
            float squaredDistanceFromCenter = originFromCenter.LengthSquared;
            float radiusSquared = circle.Radius * circle.Radius;

            if (squaredDistanceFromCenter <= radiusSquared + epsilon)
            {
                return RaycastResult2D.CreateHit(ray.Origin, -ray.Direction, 0f, true);
            }

            float directionProjection = Vec2D.Dot(originFromCenter, ray.Direction);

            if (directionProjection >= 0f)
            {
                return RaycastResult2D.NoHit;
            }

            float discriminant = directionProjection * directionProjection
                - (squaredDistanceFromCenter - radiusSquared);

            if (discriminant < -epsilon)
            {
                return RaycastResult2D.NoHit;
            }

            float distance = -directionProjection
                - (float)Math.Sqrt(Math.Max(0f, discriminant));

            if (distance < -epsilon || distance > maximumDistance + epsilon)
            {
                return RaycastResult2D.NoHit;
            }

            float safeDistance = CollisionMath2D.Clamp(distance, 0f, maximumDistance);
            Vec2D point = ray.Origin + ray.Direction * safeDistance;
            Vec2D normal = point - circle.Center;

            if (!normal.TryNormalize(out Vec2D normalizedNormal, epsilon))
            {
                normalizedNormal = -ray.Direction;
            }

            return RaycastResult2D.CreateHit(
                point,
                normalizedNormal,
                safeDistance,
                false);
        }

        public static RaycastResult2D Raycast(
            Ray2D ray,
            Obb2D box,
            float maximumDistance,
            float epsilon = CollisionMath2D.DefaultEpsilon)
        {
            ValidateMaximumDistance(maximumDistance);
            CollisionMath2D.ValidateEpsilon(epsilon);

            if (!ray.Direction.TryNormalize(out _, epsilon))
            {
                throw new ArgumentException("Ray direction must not be near zero.", nameof(ray));
            }

            Vec2D originOffset = ray.Origin - box.Center;
            float localOriginX = Vec2D.Dot(originOffset, box.AxisX);
            float localOriginY = Vec2D.Dot(originOffset, box.AxisY);
            float localDirectionX = Vec2D.Dot(ray.Direction, box.AxisX);
            float localDirectionY = Vec2D.Dot(ray.Direction, box.AxisY);

            bool startedInside = localOriginX >= -box.HalfExtents.X - epsilon
                && localOriginX <= box.HalfExtents.X + epsilon
                && localOriginY >= -box.HalfExtents.Y - epsilon
                && localOriginY <= box.HalfExtents.Y + epsilon;

            if (startedInside)
            {
                return RaycastResult2D.CreateHit(ray.Origin, -ray.Direction, 0f, true);
            }

            float enterDistance = 0f;
            float exitDistance = maximumDistance;
            Vec2D localEnterNormal = Vec2D.Zero;

            if (!UpdateRaySlab(
                    localOriginX,
                    localDirectionX,
                    -box.HalfExtents.X,
                    box.HalfExtents.X,
                    Vec2D.UnitX,
                    epsilon,
                    ref enterDistance,
                    ref exitDistance,
                    ref localEnterNormal)
                || !UpdateRaySlab(
                    localOriginY,
                    localDirectionY,
                    -box.HalfExtents.Y,
                    box.HalfExtents.Y,
                    Vec2D.UnitY,
                    epsilon,
                    ref enterDistance,
                    ref exitDistance,
                    ref localEnterNormal)
                || enterDistance < -epsilon
                || enterDistance > maximumDistance + epsilon)
            {
                return RaycastResult2D.NoHit;
            }

            float safeDistance = CollisionMath2D.Clamp(enterDistance, 0f, maximumDistance);
            Vec2D worldNormal = box.AxisX * localEnterNormal.X
                + box.AxisY * localEnterNormal.Y;
            Vec2D hitPoint = ray.Origin + ray.Direction * safeDistance;
            return RaycastResult2D.CreateHit(hitPoint, worldNormal, safeDistance, false);
        }

        private static Vec2D NormalizeAxis(Vec2D axis, float epsilon)
        {
            if (!axis.TryNormalize(out Vec2D normalizedAxis, epsilon))
            {
                throw new ArgumentException("Projection axis must not be near zero.", nameof(axis));
            }

            return normalizedAxis;
        }

        private static void ValidateMaximumDistance(float maximumDistance)
        {
            if (!CollisionMath2D.IsFinite(maximumDistance) || maximumDistance < 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maximumDistance),
                    "Raycast maximum distance must be finite and non-negative.");
            }
        }

        private static bool UpdateRaySlab(
            float origin,
            float direction,
            float minimum,
            float maximum,
            Vec2D positiveAxis,
            float epsilon,
            ref float enterDistance,
            ref float exitDistance,
            ref Vec2D enterNormal)
        {
            if (Math.Abs(direction) <= epsilon)
            {
                return origin >= minimum - epsilon && origin <= maximum + epsilon;
            }

            float nearDistance;
            float farDistance;
            Vec2D nearNormal;

            if (direction > 0f)
            {
                nearDistance = (minimum - origin) / direction;
                farDistance = (maximum - origin) / direction;
                nearNormal = -positiveAxis;
            }
            else
            {
                nearDistance = (maximum - origin) / direction;
                farDistance = (minimum - origin) / direction;
                nearNormal = positiveAxis;
            }

            if (nearDistance > enterDistance)
            {
                enterDistance = nearDistance;
                enterNormal = nearNormal;
            }

            if (farDistance < exitDistance)
            {
                exitDistance = farDistance;
            }

            return enterDistance <= exitDistance + epsilon;
        }

        private static bool TryEvaluateAxis(
            Vec2D firstCenter,
            Vec2D secondCenter,
            ProjectionInterval2D first,
            ProjectionInterval2D second,
            Vec2D axis,
            float epsilon,
            out Vec2D normal,
            out float penetrationDepth)
        {
            if (AreSeparated(first, second, epsilon))
            {
                normal = Vec2D.Zero;
                penetrationDepth = 0f;
                return false;
            }

            float movePositive = second.Maximum - first.Minimum;
            float moveNegative = first.Maximum - second.Minimum;

            if (movePositive < moveNegative - epsilon)
            {
                normal = axis;
                penetrationDepth = Math.Max(0f, movePositive);
                return true;
            }

            if (moveNegative < movePositive - epsilon)
            {
                normal = -axis;
                penetrationDepth = Math.Max(0f, moveNegative);
                return true;
            }

            float centerDirection = Vec2D.Dot(firstCenter - secondCenter, axis);
            normal = centerDirection < 0f ? -axis : axis;
            penetrationDepth = Math.Max(0f, Math.Min(movePositive, moveNegative));
            return true;
        }

        private static bool TryEvaluateObbAxis(
            Obb2D first,
            Obb2D second,
            Vec2D axis,
            float epsilon,
            out Vec2D normal,
            out float penetrationDepth)
        {
            return TryEvaluateAxis(
                first.Center,
                second.Center,
                Project(first, axis, epsilon),
                Project(second, axis, epsilon),
                axis,
                epsilon,
                out normal,
                out penetrationDepth);
        }

        private static void SelectShallowerAxis(
            Vec2D candidateNormal,
            float candidateDepth,
            float epsilon,
            ref Vec2D bestNormal,
            ref float bestDepth)
        {
            if (candidateDepth < bestDepth - epsilon)
            {
                bestNormal = candidateNormal;
                bestDepth = candidateDepth;
            }
        }

        private static Vec2D GetOutsideFallbackNormal(float localX, float localY, Obb2D box)
        {
            float outsideX = Math.Max(0f, Math.Abs(localX) - box.HalfExtents.X);
            float outsideY = Math.Max(0f, Math.Abs(localY) - box.HalfExtents.Y);

            if (outsideX >= outsideY)
            {
                return localX < 0f ? -box.AxisX : box.AxisX;
            }

            return localY < 0f ? -box.AxisY : box.AxisY;
        }

        private static void GetNearestInsideFace(
            float localX,
            float localY,
            Obb2D box,
            out Vec2D normal,
            out float distanceToFace)
        {
            distanceToFace = box.HalfExtents.X - localX;
            normal = box.AxisX;

            float candidateDistance = box.HalfExtents.X + localX;

            if (candidateDistance < distanceToFace)
            {
                distanceToFace = candidateDistance;
                normal = -box.AxisX;
            }

            candidateDistance = box.HalfExtents.Y - localY;

            if (candidateDistance < distanceToFace)
            {
                distanceToFace = candidateDistance;
                normal = box.AxisY;
            }

            candidateDistance = box.HalfExtents.Y + localY;

            if (candidateDistance < distanceToFace)
            {
                distanceToFace = candidateDistance;
                normal = -box.AxisY;
            }
        }
    }
}
