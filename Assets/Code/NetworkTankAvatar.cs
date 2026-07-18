using UnityEngine;
using System.Collections.Generic;

public class NetworkTankAvatar : MonoBehaviour
{
    [Header("Tank parts")]
    [SerializeField] private Transform bodyTransform;
    [SerializeField] private Transform tankTower;
    [SerializeField] private Transform aimTransform;

    private Rigidbody tankRigidbody;
    private bool useNetworkAuthorityMode;
    private bool hasPendingServerState;
    private Vector3 pendingPosition;
    private Quaternion pendingRotation;
    private float pendingAimX;
    private float pendingAimZ;
    private bool useRemoteInterpolation;
    private int interpolationDelayTicks = 3;
    private float interpolationTickRate = 30f;
    private int maxBufferedSnapshots = 8;
    private bool hasLocalPredictionCorrectionTarget;
    private Vector3 localCorrectionTargetPosition;
    private Quaternion localCorrectionTargetRotation;
    private float localCorrectionTargetAimX;
    private float localCorrectionTargetAimZ;
    private float localCorrectionSpeed = 10f;
    private float localCorrectionStopDistance = 0.02f;
    private readonly List<BufferedServerState> remoteSnapshotBuffer = new List<BufferedServerState>();

    private void Awake()
    {
        if (bodyTransform == null)
        {
            bodyTransform = transform;
        }

        tankRigidbody = GetComponent<Rigidbody>();
    }

    private void FixedUpdate()
    {
        if (!hasPendingServerState)
        {
            return;
        }

        hasPendingServerState = false;
        ApplyBodyState(pendingPosition, pendingRotation);
        ApplyAimState(pendingAimX, pendingAimZ);
    }

    private void Update()
    {
        if (!useRemoteInterpolation)
        {
            SmoothLocalPredictionCorrection();
            return;
        }

        PlayRemoteSnapshotBuffer();
    }

    public void SetNetworkAuthorityMode(bool isEnabled)
    {
        useNetworkAuthorityMode = isEnabled;

        if (tankRigidbody == null)
        {
            tankRigidbody = GetComponent<Rigidbody>();
        }

        if (tankRigidbody == null)
        {
            return;
        }

        tankRigidbody.isKinematic = isEnabled;
        tankRigidbody.velocity = Vector3.zero;
        tankRigidbody.angularVelocity = Vector3.zero;
    }

    public void ApplyServerState(float x, float y, float z, float bodyYaw, float aimX, float aimZ)
    {
        pendingPosition = new Vector3(x, y, z);
        pendingRotation = Quaternion.Euler(0f, bodyYaw, 0f);
        pendingAimX = aimX;
        pendingAimZ = aimZ;
        hasPendingServerState = true;
    }

    public void ApplyServerStateImmediately(Vector3 position, Quaternion rotation, float aimX, float aimZ)
    {
        hasPendingServerState = false;
        hasLocalPredictionCorrectionTarget = false;
        ApplyBodyStateDirectly(position, rotation);
        ApplyAimState(aimX, aimZ);
    }

    public void SmoothToPredictedServerState(
        Vector3 position,
        Quaternion rotation,
        float aimX,
        float aimZ,
        float correctionSpeed,
        float stopDistance)
    {
        localCorrectionTargetPosition = position;
        localCorrectionTargetRotation = rotation;
        localCorrectionTargetAimX = aimX;
        localCorrectionTargetAimZ = aimZ;
        localCorrectionSpeed = Mathf.Max(0.1f, correctionSpeed);
        localCorrectionStopDistance = Mathf.Max(0.001f, stopDistance);
        hasLocalPredictionCorrectionTarget = true;
    }

    public void SetRemoteInterpolation(bool isEnabled, int delayTicks, float tickRate, int maxSnapshots)
    {
        useRemoteInterpolation = isEnabled;
        interpolationDelayTicks = Mathf.Max(1, delayTicks);
        interpolationTickRate = Mathf.Max(1f, tickRate);
        maxBufferedSnapshots = Mathf.Max(2, maxSnapshots);

        if (!useRemoteInterpolation)
        {
            remoteSnapshotBuffer.Clear();
        }
    }

    public void AddRemoteSnapshot(int serverTick, float x, float y, float z, float bodyYaw, float aimX, float aimZ)
    {
        BufferedServerState snapshot = new BufferedServerState
        {
            ServerTick = serverTick,
            Position = new Vector3(x, y, z),
            Rotation = Quaternion.Euler(0f, bodyYaw, 0f),
            AimX = aimX,
            AimZ = aimZ,
            ReceivedTime = Time.time
        };

        int insertIndex = remoteSnapshotBuffer.Count;

        for (int i = 0; i < remoteSnapshotBuffer.Count; i++)
        {
            if (remoteSnapshotBuffer[i].ServerTick == serverTick)
            {
                remoteSnapshotBuffer[i] = snapshot;
                return;
            }

            if (remoteSnapshotBuffer[i].ServerTick > serverTick)
            {
                insertIndex = i;
                break;
            }
        }

        remoteSnapshotBuffer.Insert(insertIndex, snapshot);

        while (remoteSnapshotBuffer.Count > maxBufferedSnapshots)
        {
            remoteSnapshotBuffer.RemoveAt(0);
        }
    }

    private void PlayRemoteSnapshotBuffer()
    {
        if (remoteSnapshotBuffer.Count == 0)
        {
            return;
        }

        if (remoteSnapshotBuffer.Count == 1)
        {
            BufferedServerState onlySnapshot = remoteSnapshotBuffer[0];
            ApplyBodyStateDirectly(onlySnapshot.Position, onlySnapshot.Rotation);
            ApplyAimState(onlySnapshot.AimX, onlySnapshot.AimZ);
            return;
        }

        BufferedServerState newestSnapshot = remoteSnapshotBuffer[remoteSnapshotBuffer.Count - 1];
        float ticksSinceNewestArrived = (Time.time - newestSnapshot.ReceivedTime) * interpolationTickRate;
        float renderTick = newestSnapshot.ServerTick - interpolationDelayTicks + ticksSinceNewestArrived;

        BufferedServerState olderSnapshot = remoteSnapshotBuffer[0];
        BufferedServerState newerSnapshot = remoteSnapshotBuffer[remoteSnapshotBuffer.Count - 1];
        bool foundPair = false;

        for (int i = 0; i < remoteSnapshotBuffer.Count - 1; i++)
        {
            BufferedServerState current = remoteSnapshotBuffer[i];
            BufferedServerState next = remoteSnapshotBuffer[i + 1];

            if (current.ServerTick <= renderTick && renderTick <= next.ServerTick)
            {
                olderSnapshot = current;
                newerSnapshot = next;
                foundPair = true;
                break;
            }
        }

        if (!foundPair)
        {
            BufferedServerState heldSnapshot = renderTick < remoteSnapshotBuffer[0].ServerTick
                ? remoteSnapshotBuffer[0]
                : remoteSnapshotBuffer[remoteSnapshotBuffer.Count - 1];

            ApplyBodyStateDirectly(heldSnapshot.Position, heldSnapshot.Rotation);
            ApplyAimState(heldSnapshot.AimX, heldSnapshot.AimZ);
            return;
        }

        float tickRange = newerSnapshot.ServerTick - olderSnapshot.ServerTick;
        float lerpAmount = tickRange <= 0f ? 1f : (renderTick - olderSnapshot.ServerTick) / tickRange;
        Vector3 interpolatedPosition = Vector3.Lerp(olderSnapshot.Position, newerSnapshot.Position, lerpAmount);
        Quaternion interpolatedRotation = Quaternion.Slerp(olderSnapshot.Rotation, newerSnapshot.Rotation, lerpAmount);
        float interpolatedAimX = Mathf.Lerp(olderSnapshot.AimX, newerSnapshot.AimX, lerpAmount);
        float interpolatedAimZ = Mathf.Lerp(olderSnapshot.AimZ, newerSnapshot.AimZ, lerpAmount);

        ApplyBodyStateDirectly(interpolatedPosition, interpolatedRotation);
        ApplyAimState(interpolatedAimX, interpolatedAimZ);
        RemoveSnapshotsOlderThan(olderSnapshot.ServerTick);
    }

    private void SmoothLocalPredictionCorrection()
    {
        if (!hasLocalPredictionCorrectionTarget)
        {
            return;
        }

        Vector3 currentPosition = bodyTransform != null ? bodyTransform.position : transform.position;
        float distance = Vector3.Distance(currentPosition, localCorrectionTargetPosition);

        if (distance <= localCorrectionStopDistance)
        {
            ApplyServerStateImmediately(
                localCorrectionTargetPosition,
                localCorrectionTargetRotation,
                localCorrectionTargetAimX,
                localCorrectionTargetAimZ);
            return;
        }

        float lerpAmount = 1f - Mathf.Exp(-localCorrectionSpeed * Time.deltaTime);
        Vector3 smoothedPosition = Vector3.Lerp(currentPosition, localCorrectionTargetPosition, lerpAmount);
        Quaternion currentRotation = bodyTransform != null ? bodyTransform.rotation : transform.rotation;
        Quaternion smoothedRotation = Quaternion.Slerp(currentRotation, localCorrectionTargetRotation, lerpAmount);

        ApplyBodyStateDirectly(smoothedPosition, smoothedRotation);
        ApplyAimState(localCorrectionTargetAimX, localCorrectionTargetAimZ);
    }

    private void RemoveSnapshotsOlderThan(int serverTick)
    {
        while (remoteSnapshotBuffer.Count > 2 && remoteSnapshotBuffer[1].ServerTick < serverTick)
        {
            remoteSnapshotBuffer.RemoveAt(0);
        }
    }

    private void ApplyBodyState(Vector3 serverPosition, Quaternion serverRotation)
    {
        if (tankRigidbody != null && bodyTransform == transform)
        {
            if (useNetworkAuthorityMode)
            {
                tankRigidbody.MovePosition(serverPosition);
                tankRigidbody.MoveRotation(serverRotation);
            }
            else
            {
                tankRigidbody.position = serverPosition;
                tankRigidbody.rotation = serverRotation;
            }

            tankRigidbody.velocity = Vector3.zero;
            tankRigidbody.angularVelocity = Vector3.zero;
            return;
        }

        bodyTransform.position = serverPosition;
        bodyTransform.rotation = serverRotation;
    }

    private void ApplyBodyStateDirectly(Vector3 serverPosition, Quaternion serverRotation)
    {
        if (tankRigidbody != null && bodyTransform == transform)
        {
            tankRigidbody.position = serverPosition;
            tankRigidbody.rotation = serverRotation;
            tankRigidbody.velocity = Vector3.zero;
            tankRigidbody.angularVelocity = Vector3.zero;
            return;
        }

        bodyTransform.position = serverPosition;
        bodyTransform.rotation = serverRotation;
    }

    private void ApplyAimState(float aimX, float aimZ)
    {
        if (aimTransform != null)
        {
            float aimY = aimTransform.position.y;
            aimTransform.position = new Vector3(aimX, aimY, aimZ);
        }

        if (tankTower == null)
        {
            return;
        }

        Vector3 targetPoint = aimTransform != null
            ? aimTransform.position
            : new Vector3(aimX, tankTower.position.y, aimZ);

        Vector3 direction = targetPoint - tankTower.position;
        direction.y = 0f;

        if (direction.sqrMagnitude < 0.0001f)
        {
            return;
        }

        tankTower.rotation = Quaternion.LookRotation(direction);
    }

    private struct BufferedServerState
    {
        public int ServerTick;
        public Vector3 Position;
        public Quaternion Rotation;
        public float AimX;
        public float AimZ;
        public float ReceivedTime;
    }
}
