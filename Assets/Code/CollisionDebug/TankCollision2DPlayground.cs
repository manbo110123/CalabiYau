using System;
using CalabiYau.CollisionCore;
using CalabiYau.TankCollision;
using UnityEngine;

/// <summary>
/// Optional stage 16.1 manual playground. Attach only to a temporary visible object;
/// it does not participate in the networked Tank controller or Unity Rigidbody physics.
/// </summary>
public sealed class TankCollision2DPlayground : MonoBehaviour
{
    [Serializable]
    private sealed class WallDefinition
    {
        public int ColliderId = 1;
        public Vector2 Center = new Vector2(0f, 3f);
        public Vector2 HalfExtents = new Vector2(4f, 0.15f);
        public float YawDegrees;
    }

    [Header("Pure C# map (Unity X/Z)")]
    [SerializeField] private Vector2 worldCenter = Vector2.zero;
    [SerializeField] private Vector2 worldHalfExtents = new Vector2(8f, 8f);
    [SerializeField] private WallDefinition[] walls =
    {
        new WallDefinition
        {
            ColliderId = 1,
            Center = new Vector2(0f, 3f),
            HalfExtents = new Vector2(4f, 0.15f),
            YawDegrees = 0f
        },
        new WallDefinition
        {
            ColliderId = 2,
            Center = new Vector2(3f, 0f),
            HalfExtents = new Vector2(0.15f, 3f),
            YawDegrees = 0f
        }
    };

    [Header("Tank footprint and fixed-step movement")]
    [SerializeField] private Vector2 tankHalfExtents = new Vector2(0.5f, 0.75f);
    [SerializeField, Min(0f)] private float skinWidth = 0.05f;
    [SerializeField, Min(0.001f)] private float maxSubstepDistance = 0.05f;
    [SerializeField, Min(1)] private int maxMovementSubsteps = 128;
    [SerializeField, Min(1)] private int maxCollisionIterations = 8;
    [SerializeField, Min(1f)] private float simulationTickRate = 30f;
    [SerializeField, Min(0f)] private float movementSpeed = 7f;
    [SerializeField, Min(0f)] private float turnDegreesPerSecond = 180f;

    [Header("Runtime diagnostics")]
    [SerializeField] private bool lastMoveWasBlocked;
    [SerializeField] private bool lastRotationWasBlocked;
    [SerializeField] private int lastCollisionCount;
    [SerializeField] private bool reachedSafetyLimit;

    private TankWorldCollision2D solver;
    private TankPose2D pose;
    private TankPose2D initialPose;
    private float accumulator;

    private void Awake()
    {
        try
        {
            solver = BuildSolver();
            pose = ReadTransformPose();
            initialPose = pose;

            if (!solver.IsPoseValid(pose))
            {
                throw new InvalidOperationException(
                    "The playground object starts inside a wall or outside the world bounds.");
            }
        }
        catch (Exception exception)
        {
            Debug.LogError($"TankCollision2DPlayground configuration is invalid: {exception.Message}", this);
            enabled = false;
        }
    }

    private void Update()
    {
        if (solver == null)
        {
            return;
        }

        if (UnityEngine.Input.GetKeyDown(KeyCode.R))
        {
            pose = initialPose;
            accumulator = 0f;
            ApplyPoseToTransform();
        }

        float moveAxis = ReadSignedKeyAxis(KeyCode.DownArrow, KeyCode.UpArrow);
        float turnAxis = ReadSignedKeyAxis(KeyCode.LeftArrow, KeyCode.RightArrow);
        float tickDuration = 1f / Math.Max(1f, simulationTickRate);
        accumulator = Math.Min(accumulator + Time.deltaTime, 0.25f);

        while (accumulator >= tickDuration)
        {
            if (!solver.IsPoseValid(pose))
            {
                Debug.LogError(
                    "TankCollision2DPlayground pose became invalid; the optional playground has been disabled.",
                    this);
                enabled = false;
                return;
            }

            TankMoveResult2D result = solver.Move(
                pose,
                moveAxis * movementSpeed * tickDuration,
                turnAxis * turnDegreesPerSecond * Mathf.Deg2Rad * tickDuration);
            pose = result.Pose;
            lastMoveWasBlocked = result.WasBlocked;
            lastRotationWasBlocked = result.RotationBlocked;
            lastCollisionCount = result.CollisionCount;
            reachedSafetyLimit = result.ReachedSubstepLimit
                || result.ReachedCollisionIterationLimit;
            accumulator -= tickDuration;
        }

        ApplyPoseToTransform();
    }

    private TankWorldCollision2D BuildSolver()
    {
        WallDefinition[] definitions = walls ?? Array.Empty<WallDefinition>();
        StaticCollider2D[] colliders = new StaticCollider2D[definitions.Length];

        for (int index = 0; index < definitions.Length; index++)
        {
            WallDefinition wall = definitions[index]
                ?? throw new ArgumentException($"Wall entry {index} is null.");
            colliders[index] = StaticCollider2D.FromGameplayYaw(
                wall.ColliderId,
                ToCore(wall.Center),
                ToCore(wall.HalfExtents),
                wall.YawDegrees * Mathf.Deg2Rad);
        }

        TankCollisionMap2D map = new TankCollisionMap2D(
            new Aabb2D(ToCore(worldCenter), ToCore(worldHalfExtents)),
            colliders);
        TankCollisionSettings2D collisionSettings = new TankCollisionSettings2D(
            ToCore(tankHalfExtents),
            skinWidth,
            maxSubstepDistance,
            maxMovementSubsteps,
            maxCollisionIterations);
        return new TankWorldCollision2D(map, collisionSettings);
    }

    private TankPose2D ReadTransformPose()
    {
        Vector3 position = transform.position;
        return new TankPose2D(
            new Vec2D(position.x, position.z),
            transform.eulerAngles.y * Mathf.Deg2Rad);
    }

    private void ApplyPoseToTransform()
    {
        Vector3 currentPosition = transform.position;
        transform.SetPositionAndRotation(
            new Vector3(pose.Position.X, currentPosition.y, pose.Position.Y),
            Quaternion.Euler(0f, pose.GameplayYawRadians * Mathf.Rad2Deg, 0f));
    }

    private static float ReadSignedKeyAxis(KeyCode negative, KeyCode positive)
    {
        float value = 0f;

        if (UnityEngine.Input.GetKey(negative))
        {
            value -= 1f;
        }

        if (UnityEngine.Input.GetKey(positive))
        {
            value += 1f;
        }

        return value;
    }

    private static Vec2D ToCore(Vector2 value)
    {
        return new Vec2D(value.x, value.y);
    }

    private void OnDrawGizmosSelected()
    {
        float drawHeight = transform.position.y;
        DrawOrientedBox(
            worldCenter,
            worldHalfExtents,
            0f,
            drawHeight,
            Color.white);

        if (walls != null)
        {
            for (int index = 0; index < walls.Length; index++)
            {
                WallDefinition wall = walls[index];

                if (wall != null)
                {
                    DrawOrientedBox(
                        wall.Center,
                        wall.HalfExtents,
                        wall.YawDegrees,
                        drawHeight,
                        Color.cyan);
                }
            }
        }

        Vector3 tankPosition = transform.position;
        Vector2 tankCenter = new Vector2(tankPosition.x, tankPosition.z);
        Vector2 expandedHalfExtents = new Vector2(
            tankHalfExtents.x + skinWidth,
            tankHalfExtents.y + skinWidth);
        DrawOrientedBox(
            tankCenter,
            expandedHalfExtents,
            transform.eulerAngles.y,
            drawHeight,
            lastMoveWasBlocked ? Color.red : Color.green);
    }

    private static void DrawOrientedBox(
        Vector2 center,
        Vector2 halfExtents,
        float yawDegrees,
        float height,
        Color color)
    {
        Matrix4x4 previousMatrix = Gizmos.matrix;
        Color previousColor = Gizmos.color;
        Gizmos.color = color;
        Gizmos.matrix = Matrix4x4.TRS(
            new Vector3(center.x, height, center.y),
            Quaternion.Euler(0f, yawDegrees, 0f),
            new Vector3(
                Math.Max(0f, halfExtents.x * 2f),
                0.05f,
                Math.Max(0f, halfExtents.y * 2f)));
        Gizmos.DrawWireCube(Vector3.zero, Vector3.one);
        Gizmos.matrix = previousMatrix;
        Gizmos.color = previousColor;
    }
}
