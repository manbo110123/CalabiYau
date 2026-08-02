using UnityEngine;
using System.Collections.Generic;

public class NetworkTankAvatar : MonoBehaviour
{
    [Header("Tank parts")]
    [SerializeField] private Transform bodyTransform;
    [SerializeField] private Transform tankTower;
    [SerializeField] private Transform aimTransform;
    [SerializeField] private TankWeapon tankWeapon;

    [Header("Combat feedback")]
    [SerializeField] private Color hitFlashColor = Color.red;
    [SerializeField] private float hitFlashSeconds = 0.15f;
    [SerializeField] private Color deathTintColor = Color.gray;

    [Header("Status UI")]
    [SerializeField] private bool showWorldStatus = true;
    [SerializeField] private Vector3 statusWorldOffset = new Vector3(0f, 2.2f, 0f);
    [SerializeField] private Color aliveStatusColor = Color.white;
    [SerializeField] private Color deadStatusColor = Color.red;

    private Rigidbody tankRigidbody;
    private Renderer[] renderers;
    private Color[] originalColors;
    private GUIStyle statusLabelStyle;
    private int playerId;
    private bool isLocalPlayer;
    private bool useNetworkAuthorityMode;
    private bool hasPendingServerState;
    private Vector3 pendingPosition;
    private Quaternion pendingRotation;
    private float pendingAimX;
    private float pendingAimZ;
    private bool useRemoteInterpolation;
    private int interpolationDelayTicks = 3;
    private int observedSnapshotIntervalTicks = 1;
    private int lastReceivedRemoteSnapshotTick;
    private float interpolationTickRate = 30f;
    private int maxBufferedSnapshots = 8;
    private bool hasLocalPredictionCorrectionTarget;
    private Vector3 localCorrectionTargetPosition;
    private Quaternion localCorrectionTargetRotation;
    private bool localCorrectionIncludesRotation;
    private float localCorrectionSpeed = 10f;
    private float localCorrectionMaxSeconds = 0.5f;
    private float localCorrectionStopDistance = 0.02f;
    private float localCorrectionStartedAt;
    private float hitFlashTimer;
    private int currentHealth = 100;
    private int maxHealth = 100;
    private bool isAlive = true;
    private float respawnRemainingSeconds;
    private readonly List<BufferedServerState> remoteSnapshotBuffer = new List<BufferedServerState>();

    public int CurrentHealth => currentHealth;
    public int MaxHealth => maxHealth;
    public bool IsAlive => isAlive;
    public float RespawnRemainingSeconds => respawnRemainingSeconds;
    public int RemoteSnapshotBufferCount => remoteSnapshotBuffer.Count;
    public int EffectiveRemoteInterpolationDelayTicks => Mathf.Max(interpolationDelayTicks, observedSnapshotIntervalTicks + 1);
    public bool HasLocalPredictionCorrection => hasLocalPredictionCorrectionTarget;

    private void Awake()
    {
        if (bodyTransform == null)
        {
            bodyTransform = transform;
        }

        if (tankWeapon == null)
        {
            tankWeapon = GetComponent<TankWeapon>();
        }

        tankRigidbody = GetComponent<Rigidbody>();
        CacheRenderers();
    }

    private void FixedUpdate()
    {
        if (!hasPendingServerState)
        {
            return;
        }

        hasPendingServerState = false;
        ApplyBodyState(pendingPosition, pendingRotation);

        if (!isLocalPlayer)
        {
            ApplyAimState(pendingAimX, pendingAimZ);
        }
    }

    private void Update()
    {
        UpdateRespawnCountdown();
        UpdateHitFlash();

        if (!useRemoteInterpolation)
        {
            SmoothLocalPredictionCorrection();
            return;
        }

        PlayRemoteSnapshotBuffer();
    }

    private void OnGUI()
    {
        DrawWorldStatus();
    }

    public void SetPlayerInfo(int newPlayerId, bool newIsLocalPlayer)
    {
        playerId = newPlayerId;
        isLocalPlayer = newIsLocalPlayer;
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

    public void ApplyServerStateImmediately(Vector3 position, Quaternion rotation)
    {
        hasPendingServerState = false;
        hasLocalPredictionCorrectionTarget = false;
        ApplyBodyStateDirectly(position, rotation);
    }

    public bool SmoothToPredictedServerState(
        Vector3 position,
        Quaternion rotation,
        float correctionSpeed,
        float maxCorrectionSeconds,
        float stopDistance,
        bool includeRotation)
    {
        localCorrectionTargetPosition = position;
        localCorrectionTargetRotation = rotation;
        localCorrectionIncludesRotation = includeRotation;
        localCorrectionSpeed = Mathf.Max(0.1f, correctionSpeed);
        localCorrectionMaxSeconds = Mathf.Max(0.01f, maxCorrectionSeconds);
        localCorrectionStopDistance = Mathf.Max(0.001f, stopDistance);
        bool startedNewCorrection = !hasLocalPredictionCorrectionTarget;

        if (startedNewCorrection)
        {
            localCorrectionStartedAt = Time.time;
        }

        hasLocalPredictionCorrectionTarget = true;
        return startedNewCorrection;
    }

    // Used by the local prediction owner before its Rigidbody continues moving. Remote
    // avatars never call this because their transform is driven by snapshot interpolation.
    public void CancelLocalPredictionCorrection()
    {
        hasLocalPredictionCorrectionTarget = false;
    }

    public void SetRemoteInterpolation(bool isEnabled, int delayTicks, float tickRate, int maxSnapshots)
    {
        useRemoteInterpolation = isEnabled;
        interpolationDelayTicks = Mathf.Max(1, delayTicks);
        observedSnapshotIntervalTicks = 1;
        lastReceivedRemoteSnapshotTick = 0;
        interpolationTickRate = Mathf.Max(1f, tickRate);
        maxBufferedSnapshots = Mathf.Max(2, maxSnapshots);

        if (!useRemoteInterpolation)
        {
            remoteSnapshotBuffer.Clear();
        }
    }

    public void AddRemoteSnapshot(int serverTick, float x, float y, float z, float bodyYaw, float aimX, float aimZ)
    {
        // Low-priority entities can be replicated at 5 Hz while the server still ticks at
        // 30 Hz. Match the visual delay to the observed tick gap; otherwise a 3-tick buffer
        // runs dry between 6-tick updates and the avatar alternates between holding and jumping.
        if (lastReceivedRemoteSnapshotTick > 0 && serverTick > lastReceivedRemoteSnapshotTick)
        {
            observedSnapshotIntervalTicks = serverTick - lastReceivedRemoteSnapshotTick;
        }

        if (serverTick > lastReceivedRemoteSnapshotTick)
        {
            lastReceivedRemoteSnapshotTick = serverTick;
        }

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

    public void PlayNetworkFire(Vector3 origin, Vector3 direction, float range)
    {
        if (tankWeapon != null)
        {
            tankWeapon.PlayNetworkFire(origin, direction, range);
            return;
        }

        Debug.DrawRay(origin, direction.normalized * range, Color.yellow, 0.5f);
    }

    public void PlayHitFeedback(Vector3 hitPoint, int damage)
    {
        hitFlashTimer = Mathf.Max(hitFlashTimer, hitFlashSeconds);
        SetRendererColor(hitFlashColor);
        Debug.Log($"{name} hit for {damage} at {hitPoint}");
    }

    public void ApplyNetworkHealth(int health, int newMaxHealth, bool newIsAlive)
    {
        bool changed = currentHealth != health || maxHealth != Mathf.Max(1, newMaxHealth) || isAlive != newIsAlive;

        currentHealth = health;
        maxHealth = Mathf.Max(1, newMaxHealth);
        isAlive = newIsAlive;

        if (changed)
        {
            if (hitFlashTimer <= 0f)
            {
                ApplyBaseRendererColor();
            }

            Debug.Log($"{name} health={currentHealth}/{maxHealth}, alive={isAlive}");
        }
    }

    public void SetRespawnRemainingSeconds(float seconds)
    {
        respawnRemainingSeconds = Mathf.Max(0f, seconds);
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
        float renderTick = newestSnapshot.ServerTick - EffectiveRemoteInterpolationDelayTicks + ticksSinceNewestArrived;

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
            Quaternion finalRotation = localCorrectionIncludesRotation
                ? localCorrectionTargetRotation
                : (bodyTransform != null ? bodyTransform.rotation : transform.rotation);
            ApplyServerStateImmediately(localCorrectionTargetPosition, finalRotation);
            return;
        }

        if (Time.time - localCorrectionStartedAt >= localCorrectionMaxSeconds)
        {
            Quaternion finalRotation = localCorrectionIncludesRotation
                ? localCorrectionTargetRotation
                : (bodyTransform != null ? bodyTransform.rotation : transform.rotation);
            ApplyServerStateImmediately(localCorrectionTargetPosition, finalRotation);
            return;
        }

        float lerpAmount = 1f - Mathf.Exp(-localCorrectionSpeed * Time.deltaTime);
        Vector3 smoothedPosition = Vector3.Lerp(currentPosition, localCorrectionTargetPosition, lerpAmount);
        Quaternion currentRotation = bodyTransform != null ? bodyTransform.rotation : transform.rotation;
        Quaternion smoothedRotation = localCorrectionIncludesRotation
            ? Quaternion.Slerp(currentRotation, localCorrectionTargetRotation, lerpAmount)
            : currentRotation;

        ApplyBodyStateDirectly(smoothedPosition, smoothedRotation);
    }

    private void CacheRenderers()
    {
        renderers = GetComponentsInChildren<Renderer>();
        originalColors = new Color[renderers.Length];

        for (int i = 0; i < renderers.Length; i++)
        {
            originalColors[i] = renderers[i].material.color;
        }
    }

    private void UpdateHitFlash()
    {
        if (hitFlashTimer <= 0f)
        {
            return;
        }

        hitFlashTimer -= Time.deltaTime;

        if (hitFlashTimer <= 0f)
        {
            ApplyBaseRendererColor();
        }
    }

    private void UpdateRespawnCountdown()
    {
        if (isAlive || respawnRemainingSeconds <= 0f)
        {
            return;
        }

        respawnRemainingSeconds = Mathf.Max(0f, respawnRemainingSeconds - Time.deltaTime);
    }

    private void SetRendererColor(Color color)
    {
        if (renderers == null)
        {
            return;
        }

        for (int i = 0; i < renderers.Length; i++)
        {
            renderers[i].material.color = color;
        }
    }

    private void ApplyBaseRendererColor()
    {
        if (!isAlive)
        {
            SetRendererColor(deathTintColor);
            return;
        }

        RestoreRendererColors();
    }

    private void RestoreRendererColors()
    {
        if (renderers == null || originalColors == null)
        {
            return;
        }

        int count = Mathf.Min(renderers.Length, originalColors.Length);

        for (int i = 0; i < count; i++)
        {
            renderers[i].material.color = originalColors[i];
        }
    }

    private void DrawWorldStatus()
    {
        if (!showWorldStatus || Camera.main == null)
        {
            return;
        }

        Vector3 anchorPosition = (bodyTransform != null ? bodyTransform.position : transform.position) + statusWorldOffset;
        Vector3 screenPosition = Camera.main.WorldToScreenPoint(anchorPosition);

        if (screenPosition.z <= 0f)
        {
            return;
        }

        EnsureStatusLabelStyle();

        string playerLabel = playerId > 0 ? $"P{playerId}" : "P?";

        if (isLocalPlayer)
        {
            playerLabel = $"You {playerLabel}";
        }

        string statusText = isAlive
            ? $"{playerLabel}  HP {currentHealth}/{maxHealth}"
            : $"{playerLabel}  DEAD  {respawnRemainingSeconds:F1}s";

        statusLabelStyle.normal.textColor = isAlive ? aliveStatusColor : deadStatusColor;
        Vector2 size = statusLabelStyle.CalcSize(new GUIContent(statusText));
        Rect labelRect = new Rect(
            screenPosition.x - size.x * 0.5f,
            Screen.height - screenPosition.y - size.y * 0.5f,
            size.x,
            size.y);

        GUI.Label(labelRect, statusText, statusLabelStyle);
    }

    private void EnsureStatusLabelStyle()
    {
        if (statusLabelStyle != null)
        {
            return;
        }

        statusLabelStyle = new GUIStyle(GUI.skin.label);
        statusLabelStyle.alignment = TextAnchor.MiddleCenter;
        statusLabelStyle.fontStyle = FontStyle.Bold;
        statusLabelStyle.fontSize = 14;
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
