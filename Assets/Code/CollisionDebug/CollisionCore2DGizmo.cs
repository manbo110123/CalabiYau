using CalabiYau.CollisionCore;
using UnityEngine;
using CollisionRay2D = CalabiYau.CollisionCore.Ray2D;

namespace CalabiYau.CollisionDebug
{
    /// <summary>
    /// Optional editor visualization for phase 16.0. Add it manually to an empty GameObject;
    /// no Scene or Prefab needs to reference it for CollisionCore to work.
    /// </summary>
    [ExecuteAlways]
    public sealed class CollisionCore2DGizmo : MonoBehaviour
    {
        [Header("First OBB (local X/Z offsets)")]
        [SerializeField] private Vector2 firstCenter = new Vector2(-1f, 0f);
        [SerializeField] private Vector2 firstHalfExtents = new Vector2(1.5f, 0.75f);
        [SerializeField] private float firstYawDegrees = 20f;

        [Header("Second OBB (local X/Z offsets)")]
        [SerializeField] private Vector2 secondCenter = new Vector2(1f, 0f);
        [SerializeField] private Vector2 secondHalfExtents = new Vector2(1.25f, 0.75f);
        [SerializeField] private float secondYawDegrees = -15f;

        [Header("SAT diagnostics")]
        [SerializeField] private bool showProjectionIntervals = true;
        [SerializeField] private float projectionSpacing = 0.65f;
        [SerializeField] private float projectionHeight = 0.12f;

        [Header("Finite ray against the second OBB")]
        [SerializeField] private bool showRay = true;
        [SerializeField] private Vector2 rayOrigin = new Vector2(-4f, -2f);
        [SerializeField] private Vector2 rayDirection = new Vector2(1f, 0.35f);
        [SerializeField] private float rayMaximumDistance = 10f;

        private void OnDrawGizmos()
        {
            Vec2D worldOffset = new Vec2D(transform.position.x, transform.position.z);
            Obb2D first = CreateObb(worldOffset, firstCenter, firstHalfExtents, firstYawDegrees);
            Obb2D second = CreateObb(worldOffset, secondCenter, secondHalfExtents, secondYawDegrees);
            OverlapResult2D overlap = CollisionQueries2D.Overlap(first, second);

            DrawObb(first, new Color(0f, 0.8f, 1f));
            DrawObb(second, overlap.Hit ? new Color(1f, 0.25f, 0.2f) : new Color(0.2f, 1f, 0.35f));
            DrawMinimumTranslation(first, overlap);

            if (showProjectionIntervals)
            {
                DrawSatDiagnostics(first, second);
            }

            if (showRay)
            {
                DrawRay(worldOffset, second);
            }
        }

        private static Obb2D CreateObb(
            Vec2D worldOffset,
            Vector2 centerOffset,
            Vector2 halfExtents,
            float yawDegrees)
        {
            Vec2D safeHalfExtents = new Vec2D(
                Mathf.Max(0f, halfExtents.x),
                Mathf.Max(0f, halfExtents.y));
            Vec2D center = worldOffset + new Vec2D(centerOffset.x, centerOffset.y);
            return new Obb2D(center, safeHalfExtents, yawDegrees * Mathf.Deg2Rad);
        }

        private void DrawObb(Obb2D box, Color color)
        {
            Vec2D axisX = box.AxisX * box.HalfExtents.X;
            Vec2D axisY = box.AxisY * box.HalfExtents.Y;
            Vec2D firstCorner = box.Center - axisX - axisY;
            Vec2D secondCorner = box.Center + axisX - axisY;
            Vec2D thirdCorner = box.Center + axisX + axisY;
            Vec2D fourthCorner = box.Center - axisX + axisY;

            Gizmos.color = color;
            Gizmos.DrawLine(ToWorld(firstCorner), ToWorld(secondCorner));
            Gizmos.DrawLine(ToWorld(secondCorner), ToWorld(thirdCorner));
            Gizmos.DrawLine(ToWorld(thirdCorner), ToWorld(fourthCorner));
            Gizmos.DrawLine(ToWorld(fourthCorner), ToWorld(firstCorner));
        }

        private void DrawMinimumTranslation(Obb2D first, OverlapResult2D overlap)
        {
            if (!overlap.Hit)
            {
                return;
            }

            float visibleLength = Mathf.Max(0.4f, overlap.PenetrationDepth);
            Vec2D end = first.Center + overlap.Normal * visibleLength;
            Gizmos.color = Color.red;
            Gizmos.DrawLine(ToWorld(first.Center, 0.08f), ToWorld(end, 0.08f));
            Gizmos.DrawSphere(ToWorld(end, 0.08f), 0.08f);
        }

        private void DrawSatDiagnostics(Obb2D first, Obb2D second)
        {
            Vec2D[] axes = { first.AxisX, first.AxisY, second.AxisX, second.AxisY };
            float safeSpacing = Mathf.Max(0.1f, projectionSpacing);
            Vec2D midpoint = (first.Center + second.Center) * 0.5f;

            for (int index = 0; index < axes.Length; index++)
            {
                Vec2D axis = axes[index];
                Vec2D perpendicular = axis.PerpendicularLeft;
                ProjectionInterval2D firstProjection = CollisionQueries2D.Project(first, axis);
                ProjectionInterval2D secondProjection = CollisionQueries2D.Project(second, axis);
                bool separated = CollisionQueries2D.AreSeparated(firstProjection, secondProjection);
                Vec2D anchorOffset = perpendicular * Vec2D.Dot(midpoint, perpendicular);
                Vec2D perpendicularOffset = anchorOffset + perpendicular * safeSpacing * (index + 1);
                Vec2D firstLineOffset = perpendicularOffset + perpendicular * 0.06f;
                Vec2D secondLineOffset = perpendicularOffset - perpendicular * 0.06f;

                DrawProjectionInterval(firstProjection, axis, firstLineOffset, new Color(0f, 0.8f, 1f));
                DrawProjectionInterval(
                    secondProjection,
                    axis,
                    secondLineOffset,
                    separated ? Color.yellow : new Color(1f, 0.2f, 0.8f));
            }
        }

        private void DrawProjectionInterval(
            ProjectionInterval2D interval,
            Vec2D axis,
            Vec2D lineOffset,
            Color color)
        {
            Vec2D start = axis * interval.Minimum + lineOffset;
            Vec2D end = axis * interval.Maximum + lineOffset;
            float sphereRadius = 0.035f;

            Gizmos.color = color;
            Gizmos.DrawLine(ToWorld(start, projectionHeight), ToWorld(end, projectionHeight));
            Gizmos.DrawSphere(ToWorld(start, projectionHeight), sphereRadius);
            Gizmos.DrawSphere(ToWorld(end, projectionHeight), sphereRadius);
        }

        private void DrawRay(Vec2D worldOffset, Obb2D target)
        {
            Vec2D origin = worldOffset + new Vec2D(rayOrigin.x, rayOrigin.y);
            Vec2D configuredDirection = new Vec2D(rayDirection.x, rayDirection.y);

            if (!configuredDirection.TryNormalize(out Vec2D direction))
            {
                direction = Vec2D.UnitX;
            }

            CollisionRay2D ray = new CollisionRay2D(origin, direction);
            float maximumDistance = Mathf.Max(0f, rayMaximumDistance);
            RaycastResult2D hit = CollisionQueries2D.Raycast(ray, target, maximumDistance);
            Vec2D rayEnd = origin + direction * (hit.Hit ? hit.Distance : maximumDistance);

            Gizmos.color = hit.Hit ? Color.red : Color.white;
            Gizmos.DrawLine(ToWorld(origin, 0.18f), ToWorld(rayEnd, 0.18f));

            if (!hit.Hit)
            {
                return;
            }

            Gizmos.DrawSphere(ToWorld(hit.Point, 0.18f), 0.09f);
            Gizmos.DrawLine(
                ToWorld(hit.Point, 0.18f),
                ToWorld(hit.Point + hit.Normal * 0.6f, 0.18f));
        }

        private Vector3 ToWorld(Vec2D point, float heightOffset = 0.05f)
        {
            return new Vector3(point.X, transform.position.y + heightOffset, point.Y);
        }
    }
}
