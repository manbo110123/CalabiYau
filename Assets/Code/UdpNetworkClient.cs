using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

public class UdpNetworkClient : MonoBehaviour
{
    [Header("Server")]
    [SerializeField] private string serverAddress = "127.0.0.1";
    [SerializeField] private int serverPort = 7777;
    [SerializeField] private string playerName = "Player";
    [SerializeField] private bool sendHelloOnStart = true;

    [Header("Local Player")]
    [SerializeField] private TankController localTank;
    [SerializeField] private NetworkTankAvatar localAvatar;
    [SerializeField] private bool serverAuthoritativeMovement = true;
    [SerializeField] private bool enableClientPrediction = true;
    [SerializeField] private bool snapToFirstLocalServerSnapshot = true;
    [SerializeField] private bool disableLocalWeaponInNetworkMode = true;
    [SerializeField] private bool disableLocalCollisionInNetworkMode = true;

    [Header("Remote Players")]
    [SerializeField] private GameObject remoteTankPrefab;
    [SerializeField] private Transform remoteTankParent;

    [Header("Network Tick")]
    [SerializeField] private float inputTickRate = 30f;
    [SerializeField] private int localInputHistorySize = 64;

    [Header("Local Reconciliation")]
    [SerializeField] private bool enablePredictionReconciliation = true;
    [SerializeField] private float reconciliationMoveSpeed = 7f;
    [SerializeField] private float reconciliationTurnDegreesPerSecond = 180f;
    [SerializeField] private float predictionCorrectionDeadZone = 0.15f;
    [SerializeField] private float hardCorrectionBaseDistance = 1.25f;
    [SerializeField] private float rttCorrectionThresholdScale = 0.5f;
    [SerializeField] private float smoothCorrectionSpeed = 5f;
    [SerializeField] private bool smoothCorrectionWhileInputActive = false;
    [SerializeField] private float activeInputDeadZone = 0.05f;

    [Header("Remote Interpolation")]
    [SerializeField] private bool interpolateRemotePlayers = true;
    [SerializeField] private float remoteInterpolationDelaySeconds = 0.1f;
    [SerializeField] private int remoteInterpolationBufferSize = 8;

    [Header("Debug")]
    [SerializeField] private bool logJsonMessages = true;
    [SerializeField] private bool logSnapshots = false;
    [SerializeField] private bool logLocalPrediction = false;
    [SerializeField] private bool logGameplayEvents = true;

    private UdpClient udpClient;
    private int playerId;
    private int inputTick;
    private float inputTimer;
    private bool hasAppliedInitialLocalServerSnapshot;
    private bool hasAuthoritativeLocalSnapshot;
    private int lastAuthoritativeServerTick;
    private int lastProcessedLocalInputTick;
    private Vector3 lastAuthoritativeLocalPosition;
    private Quaternion lastAuthoritativeLocalRotation;
    private float lastAuthoritativeLocalAimX;
    private float lastAuthoritativeLocalAimZ;
    private int predictionCorrectionCount;
    private float lastPredictionCorrectionDistance;
    private int sentFireRequestCount;
    private int receivedGameplayEventCount;

    private readonly Dictionary<int, NetworkTankAvatar> remoteAvatars = new Dictionary<int, NetworkTankAvatar>();
    private readonly HashSet<int> warnedMissingRemotePrefab = new HashSet<int>();
    private readonly List<BufferedLocalInput> localInputHistory = new List<BufferedLocalInput>();

    public int PlayerId => playerId;
    public int LastAuthoritativeServerTick => lastAuthoritativeServerTick;
    public int LastProcessedLocalInputTick => lastProcessedLocalInputTick;
    public bool HasAuthoritativeLocalSnapshot => hasAuthoritativeLocalSnapshot;
    public Vector3 LastAuthoritativeLocalPosition => lastAuthoritativeLocalPosition;
    public Quaternion LastAuthoritativeLocalRotation => lastAuthoritativeLocalRotation;
    public float LastAuthoritativeLocalAimX => lastAuthoritativeLocalAimX;
    public float LastAuthoritativeLocalAimZ => lastAuthoritativeLocalAimZ;
    public int PendingLocalInputCount => localInputHistory.Count;
    public int PredictionCorrectionCount => predictionCorrectionCount;
    public float LastPredictionCorrectionDistance => lastPredictionCorrectionDistance;

    private void Start()
    {
        Application.runInBackground = true;

        ResolveLocalReferences();
        ApplyOfflineControlMode();
        OpenSocket();

        if (sendHelloOnStart)
        {
            SendClientHello();
        }
    }

    private void Update()
    {
        ReceivePendingMessages();
        SendInputAtNetworkTick();
    }

    private void LateUpdate()
    {
        SendFireRequestIfPressed();
    }

    private void OnApplicationQuit()
    {
        CloseSocket();
    }

    [ContextMenu("Send ClientHello")]
    public void SendClientHello()
    {
        OpenSocket();

        ClientHelloMessage message = new ClientHelloMessage();
        message.type = "ClientHello";
        message.name = playerName;

        SendJson(JsonUtility.ToJson(message));
    }

    private void ResolveLocalReferences()
    {
        if (localTank == null)
        {
            localTank = GetComponent<TankController>();
        }

        if (localTank != null && (localAvatar == null || !IsAvatarOnLocalTank(localAvatar)))
        {
            localAvatar = localTank.GetComponent<NetworkTankAvatar>();
        }

        if (localAvatar == null && localTank != null)
        {
            localAvatar = localTank.gameObject.AddComponent<NetworkTankAvatar>();
        }

        if (remoteTankParent != null && !remoteTankParent.gameObject.scene.IsValid())
        {
            Debug.LogWarning("remoteTankParent points to a Prefab asset. Remote tanks will be spawned at the scene root instead.");
            remoteTankParent = null;
        }

        if (remoteTankParent == null)
        {
            remoteTankParent = transform.parent;
        }

        if (localTank == null)
        {
            Debug.LogWarning("UdpNetworkClient needs a localTank reference before it can send PlayerInput.");
        }
    }

    private bool IsAvatarOnLocalTank(NetworkTankAvatar avatar)
    {
        return localTank != null && avatar != null && avatar.gameObject == localTank.gameObject;
    }

    private void ApplyNetworkControlMode()
    {
        if (localTank == null || !serverAuthoritativeMovement)
        {
            return;
        }

        if (enableClientPrediction)
        {
            localTank.SetLocalMovementEnabled(true);
            localTank.SetNetworkPredictionMovementSpeeds(
                reconciliationMoveSpeed,
                reconciliationTurnDegreesPerSecond);

            if (localAvatar != null)
            {
                localAvatar.SetNetworkAuthorityMode(false);
            }
        }
        else
        {
            localTank.SetLocalMovementEnabled(false);

            if (localAvatar != null)
            {
                localAvatar.SetNetworkAuthorityMode(true);
            }
        }

        if (disableLocalWeaponInNetworkMode)
        {
            localTank.SetLocalWeaponEnabled(false);
        }

        localTank.SetLocalMovementIgnoresPhysicsCollision(enableClientPrediction && disableLocalCollisionInNetworkMode);
    }

    private void ApplyOfflineControlMode()
    {
        if (localTank != null)
        {
            localTank.SetLocalControlEnabled(true);
            localTank.SetLocalMovementIgnoresPhysicsCollision(false);
        }

        if (localAvatar != null)
        {
            localAvatar.SetNetworkAuthorityMode(false);
        }
    }

    private void SendInputAtNetworkTick()
    {
        if (playerId == 0 || localTank == null)
        {
            return;
        }

        float safeTickRate = Mathf.Max(1f, inputTickRate);
        float tickInterval = 1f / safeTickRate;
        inputTimer += Time.deltaTime;

        while (inputTimer >= tickInterval)
        {
            inputTimer -= tickInterval;
            inputTick++;
            SendPlayerInput();
        }
    }

    private void SendPlayerInput()
    {
        TankInputData inputData = localTank.CurrentInput;
        Vector3 aimPoint = localTank.CurrentAimPoint;

        PlayerInputMessage message = new PlayerInputMessage();
        message.type = "PlayerInput";
        message.playerId = playerId;
        message.inputTick = inputTick;
        message.moveAxis = inputData.MoveAxis;
        message.turnAxis = inputData.TurnAxis;
        message.aimX = aimPoint.x;
        message.aimZ = aimPoint.z;
        message.fire = inputData.FirePressed;

        SaveLocalInput(message);
        SendJson(JsonUtility.ToJson(message));
    }

    private void SendFireRequestIfPressed()
    {
        if (playerId == 0 || localTank == null)
        {
            return;
        }

        TankInputData inputData = localTank.CurrentInput;

        if (!inputData.FirePressed)
        {
            return;
        }

        Vector3 aimPoint = localTank.CurrentAimPoint;
        Vector3 fireOrigin = localTank.CurrentFireOrigin;
        Vector3 fireDirection = localTank.CurrentFireDirection.normalized;

        FireRequestMessage message = new FireRequestMessage();
        message.type = "FireRequest";
        message.playerId = playerId;
        message.requestTick = inputTick;
        message.aimX = aimPoint.x;
        message.aimZ = aimPoint.z;
        message.originX = fireOrigin.x;
        message.originY = fireOrigin.y;
        message.originZ = fireOrigin.z;
        message.directionX = fireDirection.x;
        message.directionY = fireDirection.y;
        message.directionZ = fireDirection.z;

        sentFireRequestCount++;
        SendJson(JsonUtility.ToJson(message));
    }

    private void SaveLocalInput(PlayerInputMessage message)
    {
        BufferedLocalInput bufferedInput = new BufferedLocalInput
        {
            InputTick = message.inputTick,
            MoveAxis = message.moveAxis,
            TurnAxis = message.turnAxis,
            AimX = message.aimX,
            AimZ = message.aimZ,
            Fire = message.fire,
            SentTime = Time.time
        };

        localInputHistory.Add(bufferedInput);

        while (localInputHistory.Count > Mathf.Max(1, localInputHistorySize))
        {
            localInputHistory.RemoveAt(0);
        }
    }

    private void OpenSocket()
    {
        if (udpClient != null)
        {
            return;
        }

        try
        {
            udpClient = new UdpClient();
            udpClient.Connect(serverAddress, serverPort);
        }
        catch (Exception exception)
        {
            Debug.LogError($"UDP client failed to open: {exception.Message}");
            CloseSocket();
        }
    }

    private void SendJson(string json)
    {
        if (udpClient == null)
        {
            return;
        }

        byte[] bytes = Encoding.UTF8.GetBytes(json);
        udpClient.Send(bytes, bytes.Length);

        if (logJsonMessages)
        {
            Debug.Log($"UDP sent: {json}");
        }
    }

    private void ReceivePendingMessages()
    {
        if (udpClient == null)
        {
            return;
        }

        try
        {
            while (udpClient.Available > 0)
            {
                IPEndPoint remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
                byte[] bytes = udpClient.Receive(ref remoteEndPoint);
                string json = Encoding.UTF8.GetString(bytes);

                if (logJsonMessages)
                {
                    Debug.Log($"UDP received: {json}");
                }

                HandleMessage(json);
            }
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"UDP receive failed: {exception.Message}");
        }
    }

    private void HandleMessage(string json)
    {
        MessageHeader header = JsonUtility.FromJson<MessageHeader>(json);

        switch (header.type)
        {
            case "ServerWelcome":
                HandleServerWelcome(json);
                break;

            case "WorldSnapshot":
                HandleWorldSnapshot(json);
                break;

            case "FireEvent":
                HandleFireEvent(json);
                break;

            case "HitEvent":
                HandleHitEvent(json);
                break;

            case "HealthChangedEvent":
                HandleHealthChangedEvent(json);
                break;

            default:
                Debug.LogWarning($"UDP message ignored: unsupported type '{header.type}'.");
                break;
        }
    }

    private void HandleServerWelcome(string json)
    {
        ServerWelcomeMessage welcome = JsonUtility.FromJson<ServerWelcomeMessage>(json);
        playerId = welcome.playerId;
        ResetLocalPredictionState();
        ApplyNetworkControlMode();
        Debug.Log($"Connected to server. playerId={playerId}, message={welcome.message}");
    }

    private void HandleWorldSnapshot(string json)
    {
        WorldSnapshotMessage snapshot = JsonUtility.FromJson<WorldSnapshotMessage>(json);

        if (snapshot.players == null)
        {
            return;
        }

        if (logSnapshots)
        {
            Debug.Log($"WorldSnapshot tick={snapshot.serverTick}, players={snapshot.players.Length}");
        }

        for (int i = 0; i < snapshot.players.Length; i++)
        {
            ApplyPlayerSnapshot(snapshot.serverTick, snapshot.players[i]);
        }
    }

    private void HandleFireEvent(string json)
    {
        FireEventMessage fireEvent = JsonUtility.FromJson<FireEventMessage>(json);
        receivedGameplayEventCount++;

        NetworkTankAvatar shooterAvatar = GetAvatarByPlayerId(fireEvent.shooterPlayerId);

        if (shooterAvatar == null)
        {
            Debug.LogWarning($"FireEvent ignored: no avatar for shooter playerId={fireEvent.shooterPlayerId}.");
            return;
        }

        Vector3 origin = new Vector3(fireEvent.originX, fireEvent.originY, fireEvent.originZ);
        Vector3 direction = new Vector3(fireEvent.directionX, fireEvent.directionY, fireEvent.directionZ);
        shooterAvatar.PlayNetworkFire(origin, direction, fireEvent.range);

        if (logGameplayEvents)
        {
            Debug.Log(
                $"FireEvent serverTick={fireEvent.serverTick}, shooter={fireEvent.shooterPlayerId}, " +
                $"sentFireRequests={sentFireRequestCount}, receivedEvents={receivedGameplayEventCount}");
        }
    }

    private void HandleHitEvent(string json)
    {
        HitEventMessage hitEvent = JsonUtility.FromJson<HitEventMessage>(json);
        receivedGameplayEventCount++;

        NetworkTankAvatar targetAvatar = GetAvatarByPlayerId(hitEvent.targetPlayerId);

        if (targetAvatar == null)
        {
            Debug.LogWarning($"HitEvent ignored: no avatar for target playerId={hitEvent.targetPlayerId}.");
            return;
        }

        Vector3 hitPoint = new Vector3(hitEvent.hitX, hitEvent.hitY, hitEvent.hitZ);
        targetAvatar.PlayHitFeedback(hitPoint, hitEvent.damage);

        if (logGameplayEvents)
        {
            Debug.Log(
                $"HitEvent serverTick={hitEvent.serverTick}, shooter={hitEvent.shooterPlayerId}, " +
                $"target={hitEvent.targetPlayerId}, damage={hitEvent.damage}");
        }
    }

    private void HandleHealthChangedEvent(string json)
    {
        HealthChangedEventMessage healthEvent = JsonUtility.FromJson<HealthChangedEventMessage>(json);
        receivedGameplayEventCount++;

        NetworkTankAvatar avatar = GetAvatarByPlayerId(healthEvent.playerId);

        if (avatar == null)
        {
            Debug.LogWarning($"HealthChangedEvent ignored: no avatar for playerId={healthEvent.playerId}.");
            return;
        }

        avatar.ApplyNetworkHealth(healthEvent.health, healthEvent.maxHealth, healthEvent.isAlive);

        if (logGameplayEvents)
        {
            Debug.Log(
                $"HealthChangedEvent serverTick={healthEvent.serverTick}, player={healthEvent.playerId}, " +
                $"health={healthEvent.health}/{healthEvent.maxHealth}, alive={healthEvent.isAlive}");
        }
    }

    private void ApplyPlayerSnapshot(int serverTick, PlayerSnapshotMessage snapshot)
    {
        if (snapshot.playerId == playerId)
        {
            ApplyLocalSnapshot(serverTick, snapshot);
            return;
        }

        NetworkTankAvatar remoteAvatar = GetOrCreateRemoteAvatar(snapshot);

        if (remoteAvatar == null)
        {
            return;
        }

        if (interpolateRemotePlayers)
        {
            remoteAvatar.AddRemoteSnapshot(
                serverTick,
                snapshot.x,
                snapshot.y,
                snapshot.z,
                snapshot.bodyYaw,
                snapshot.aimX,
                snapshot.aimZ);
            return;
        }

        remoteAvatar.ApplyServerState(
            snapshot.x,
            snapshot.y,
            snapshot.z,
            snapshot.bodyYaw,
            snapshot.aimX,
            snapshot.aimZ);
    }

    private void ApplyLocalSnapshot(int serverTick, PlayerSnapshotMessage snapshot)
    {
        StoreAuthoritativeLocalSnapshot(serverTick, snapshot);

        if (ShouldApplyLocalSnapshotToTransform())
        {
            ApplyLocalSnapshotToTransform(snapshot);
        }
        else if (ShouldReconcileLocalPrediction())
        {
            ReconcileLocalPrediction(snapshot);
        }

        if (logLocalPrediction)
        {
            Debug.Log(
                $"Local prediction ack: serverTick={lastAuthoritativeServerTick}, " +
                $"lastProcessedInputTick={lastProcessedLocalInputTick}, " +
                $"authoritativePosition={lastAuthoritativeLocalPosition}");
        }
    }

    private bool ShouldApplyLocalSnapshotToTransform()
    {
        if (!serverAuthoritativeMovement || !enableClientPrediction)
        {
            return true;
        }

        if (snapToFirstLocalServerSnapshot && !hasAppliedInitialLocalServerSnapshot)
        {
            hasAppliedInitialLocalServerSnapshot = true;
            return true;
        }

        return false;
    }

    private bool ShouldReconcileLocalPrediction()
    {
        return serverAuthoritativeMovement
            && enableClientPrediction
            && enablePredictionReconciliation
            && localTank != null;
    }

    private void StoreAuthoritativeLocalSnapshot(int serverTick, PlayerSnapshotMessage snapshot)
    {
        hasAuthoritativeLocalSnapshot = true;
        lastAuthoritativeServerTick = serverTick;
        lastProcessedLocalInputTick = snapshot.lastProcessedInputTick;
        lastAuthoritativeLocalPosition = new Vector3(snapshot.x, snapshot.y, snapshot.z);
        lastAuthoritativeLocalRotation = Quaternion.Euler(0f, snapshot.bodyYaw, 0f);
        lastAuthoritativeLocalAimX = snapshot.aimX;
        lastAuthoritativeLocalAimZ = snapshot.aimZ;
        RemoveAcknowledgedLocalInputs(lastProcessedLocalInputTick);
    }

    private void ResetLocalPredictionState()
    {
        hasAppliedInitialLocalServerSnapshot = false;
        hasAuthoritativeLocalSnapshot = false;
        lastAuthoritativeServerTick = 0;
        lastProcessedLocalInputTick = 0;
        lastAuthoritativeLocalPosition = Vector3.zero;
        lastAuthoritativeLocalRotation = Quaternion.identity;
        lastAuthoritativeLocalAimX = 0f;
        lastAuthoritativeLocalAimZ = 0f;
        predictionCorrectionCount = 0;
        lastPredictionCorrectionDistance = 0f;
        localInputHistory.Clear();
    }

    private void RemoveAcknowledgedLocalInputs(int acknowledgedInputTick)
    {
        while (localInputHistory.Count > 0 && localInputHistory[0].InputTick <= acknowledgedInputTick)
        {
            localInputHistory.RemoveAt(0);
        }
    }

    private void ApplyLocalSnapshotToTransform(PlayerSnapshotMessage snapshot)
    {
        if (localAvatar != null)
        {
            localAvatar.ApplyServerState(
                snapshot.x,
                snapshot.y,
                snapshot.z,
                snapshot.bodyYaw,
                snapshot.aimX,
                snapshot.aimZ);
            return;
        }

        if (localTank == null)
        {
            return;
        }

        Transform tankTransform = localTank.transform;
        tankTransform.position = new Vector3(snapshot.x, snapshot.y, snapshot.z);
        tankTransform.rotation = Quaternion.Euler(0f, snapshot.bodyYaw, 0f);
    }

    private void ReconcileLocalPrediction(PlayerSnapshotMessage snapshot)
    {
        ReconciledLocalState reconciledState = BuildReconciledLocalState(snapshot);
        Vector3 currentPosition = localTank.transform.position;
        float correctionDistance = Vector3.Distance(currentPosition, reconciledState.Position);

        lastPredictionCorrectionDistance = correctionDistance;

        float adjustedDeadZone = GetAdjustedDeadZone();
        float adjustedHardCorrectionDistance = GetAdjustedHardCorrectionDistance();

        if (correctionDistance <= adjustedDeadZone)
        {
            return;
        }

        if (correctionDistance >= adjustedHardCorrectionDistance)
        {
            predictionCorrectionCount++;
            ApplyReconciledLocalStateImmediately(reconciledState);

            if (logLocalPrediction)
            {
                Debug.Log(
                    $"Prediction hard correction: distance={correctionDistance:F3}, " +
                    $"threshold={adjustedHardCorrectionDistance:F3}, " +
                    $"pendingInputs={localInputHistory.Count}");
            }

            return;
        }

        if (!smoothCorrectionWhileInputActive && IsLocalMovementInputActive())
        {
            if (logLocalPrediction)
            {
                Debug.Log(
                    $"Prediction smooth correction delayed while input is active: " +
                    $"distance={correctionDistance:F3}, " +
                    $"hardThreshold={adjustedHardCorrectionDistance:F3}, " +
                    $"pendingInputs={localInputHistory.Count}");
            }

            return;
        }

        predictionCorrectionCount++;
        SmoothToReconciledLocalState(reconciledState, adjustedDeadZone);

        if (logLocalPrediction)
        {
            Debug.Log(
                $"Prediction smooth correction: distance={correctionDistance:F3}, " +
                $"hardThreshold={adjustedHardCorrectionDistance:F3}, " +
                $"pendingInputs={localInputHistory.Count}");
        }
    }

    private bool IsLocalMovementInputActive()
    {
        if (localTank == null)
        {
            return false;
        }

        TankInputData currentInput = localTank.CurrentInput;
        return Mathf.Abs(currentInput.MoveAxis) > activeInputDeadZone
            || Mathf.Abs(currentInput.TurnAxis) > activeInputDeadZone;
    }

    private ReconciledLocalState BuildReconciledLocalState(PlayerSnapshotMessage snapshot)
    {
        Vector3 position = new Vector3(snapshot.x, snapshot.y, snapshot.z);
        float yaw = snapshot.bodyYaw;
        float aimX = snapshot.aimX;
        float aimZ = snapshot.aimZ;
        float tickDeltaTime = 1f / Mathf.Max(1f, inputTickRate);

        for (int i = 0; i < localInputHistory.Count; i++)
        {
            ReplayBufferedInput(localInputHistory[i], tickDeltaTime, ref position, ref yaw, ref aimX, ref aimZ);
        }

        ReplayCurrentPartialInput(tickDeltaTime, ref position, ref yaw, ref aimX, ref aimZ);

        ReconciledLocalState reconciledState = new ReconciledLocalState
        {
            Position = position,
            Rotation = Quaternion.Euler(0f, yaw, 0f),
            AimX = aimX,
            AimZ = aimZ
        };

        return reconciledState;
    }

    private void ReplayBufferedInput(
        BufferedLocalInput input,
        float deltaTime,
        ref Vector3 position,
        ref float yaw,
        ref float aimX,
        ref float aimZ)
    {
        float moveAxis = Mathf.Clamp(input.MoveAxis, -1f, 1f);
        float turnAxis = Mathf.Clamp(input.TurnAxis, -1f, 1f);

        yaw += turnAxis * reconciliationTurnDegreesPerSecond * deltaTime;

        float yawRadians = yaw * Mathf.Deg2Rad;
        Vector3 forward = new Vector3(Mathf.Sin(yawRadians), 0f, Mathf.Cos(yawRadians));
        position += forward * reconciliationMoveSpeed * moveAxis * deltaTime;

        aimX = input.AimX;
        aimZ = input.AimZ;
    }

    private void ReplayCurrentPartialInput(
        float tickDeltaTime,
        ref Vector3 position,
        ref float yaw,
        ref float aimX,
        ref float aimZ)
    {
        if (localTank == null)
        {
            return;
        }

        float partialDeltaTime = Mathf.Clamp(inputTimer, 0f, tickDeltaTime);

        if (partialDeltaTime <= 0f)
        {
            return;
        }

        TankInputData currentInput = localTank.CurrentInput;
        Vector3 currentAimPoint = localTank.CurrentAimPoint;
        BufferedLocalInput partialInput = new BufferedLocalInput
        {
            MoveAxis = currentInput.MoveAxis,
            TurnAxis = currentInput.TurnAxis,
            AimX = currentAimPoint.x,
            AimZ = currentAimPoint.z,
            Fire = currentInput.FirePressed,
            SentTime = Time.time
        };

        ReplayBufferedInput(partialInput, partialDeltaTime, ref position, ref yaw, ref aimX, ref aimZ);
    }

    private float GetAdjustedDeadZone()
    {
        float estimatedRttSeconds = EstimateLocalRttSeconds();
        float rttDistance = estimatedRttSeconds * reconciliationMoveSpeed;
        return Mathf.Max(0.001f, predictionCorrectionDeadZone + rttDistance * 0.02f);
    }

    private float GetAdjustedHardCorrectionDistance()
    {
        float estimatedRttSeconds = EstimateLocalRttSeconds();
        float rttDistance = estimatedRttSeconds * reconciliationMoveSpeed;
        return Mathf.Max(0.01f, hardCorrectionBaseDistance + rttDistance * rttCorrectionThresholdScale);
    }

    private float EstimateLocalRttSeconds()
    {
        int pendingInputTicks = Mathf.Max(0, inputTick - lastProcessedLocalInputTick);
        return pendingInputTicks / Mathf.Max(1f, inputTickRate);
    }

    private void ApplyReconciledLocalStateImmediately(ReconciledLocalState reconciledState)
    {
        if (localAvatar != null)
        {
            localAvatar.ApplyServerStateImmediately(
                reconciledState.Position,
                reconciledState.Rotation,
                reconciledState.AimX,
                reconciledState.AimZ);
            return;
        }

        localTank.transform.position = reconciledState.Position;
        localTank.transform.rotation = reconciledState.Rotation;
    }

    private void SmoothToReconciledLocalState(ReconciledLocalState reconciledState, float stopDistance)
    {
        if (localAvatar != null)
        {
            localAvatar.SmoothToPredictedServerState(
                reconciledState.Position,
                reconciledState.Rotation,
                reconciledState.AimX,
                reconciledState.AimZ,
                smoothCorrectionSpeed,
                stopDistance);
            return;
        }

        localTank.transform.position = Vector3.Lerp(
            localTank.transform.position,
            reconciledState.Position,
            Time.deltaTime * smoothCorrectionSpeed);
        localTank.transform.rotation = Quaternion.Slerp(
            localTank.transform.rotation,
            reconciledState.Rotation,
            Time.deltaTime * smoothCorrectionSpeed);
    }

    private NetworkTankAvatar GetOrCreateRemoteAvatar(PlayerSnapshotMessage snapshot)
    {
        if (remoteAvatars.TryGetValue(snapshot.playerId, out NetworkTankAvatar existingAvatar))
        {
            return existingAvatar;
        }

        if (remoteTankPrefab == null)
        {
            if (!warnedMissingRemotePrefab.Contains(snapshot.playerId))
            {
                warnedMissingRemotePrefab.Add(snapshot.playerId);
                Debug.LogWarning($"Cannot show remote player {snapshot.playerId}: remoteTankPrefab is not assigned.");
            }

            return null;
        }

        Vector3 spawnPosition = new Vector3(snapshot.x, snapshot.y, snapshot.z);
        Quaternion spawnRotation = Quaternion.Euler(0f, snapshot.bodyYaw, 0f);
        GameObject remoteObject = Instantiate(remoteTankPrefab, spawnPosition, spawnRotation, remoteTankParent);

        DisableLocalGameplay(remoteObject);

        NetworkTankAvatar avatar = remoteObject.GetComponent<NetworkTankAvatar>();

        if (avatar == null)
        {
            avatar = remoteObject.AddComponent<NetworkTankAvatar>();
        }

        avatar.SetNetworkAuthorityMode(true);
        avatar.SetRemoteInterpolation(
            interpolateRemotePlayers,
            GetRemoteInterpolationDelayTicks(),
            Mathf.Max(1f, inputTickRate),
            remoteInterpolationBufferSize);
        remoteAvatars.Add(snapshot.playerId, avatar);
        return avatar;
    }

    private NetworkTankAvatar GetAvatarByPlayerId(int targetPlayerId)
    {
        if (targetPlayerId == playerId)
        {
            return localAvatar;
        }

        if (remoteAvatars.TryGetValue(targetPlayerId, out NetworkTankAvatar remoteAvatar))
        {
            return remoteAvatar;
        }

        return null;
    }

    private int GetRemoteInterpolationDelayTicks()
    {
        float safeTickRate = Mathf.Max(1f, inputTickRate);
        return Mathf.Max(1, Mathf.RoundToInt(remoteInterpolationDelaySeconds * safeTickRate));
    }

    private void DisableLocalGameplay(GameObject tankObject)
    {
        TankController tankController = tankObject.GetComponent<TankController>();

        if (tankController != null)
        {
            tankController.enabled = false;
        }

        TankInput tankInput = tankObject.GetComponent<TankInput>();

        if (tankInput != null)
        {
            tankInput.enabled = false;
        }
    }

    private void CloseSocket()
    {
        if (udpClient == null)
        {
            return;
        }

        udpClient.Close();
        udpClient = null;
    }
}

[Serializable]
public class ClientHelloMessage
{
    public string type;
    public string name;
}

[Serializable]
public class MessageHeader
{
    public string type;
}

[Serializable]
public class ServerWelcomeMessage
{
    public string type;
    public int playerId;
    public string message;
}

[Serializable]
public class PlayerInputMessage
{
    public string type;
    public int playerId;
    public int inputTick;
    public float moveAxis;
    public float turnAxis;
    public float aimX;
    public float aimZ;
    public bool fire;
}

[Serializable]
public class FireRequestMessage
{
    public string type;
    public int playerId;
    public int requestTick;
    public float aimX;
    public float aimZ;
    public float originX;
    public float originY;
    public float originZ;
    public float directionX;
    public float directionY;
    public float directionZ;
}

[Serializable]
public class WorldSnapshotMessage
{
    public string type;
    public int serverTick;
    public PlayerSnapshotMessage[] players;
}

[Serializable]
public class PlayerSnapshotMessage
{
    public int playerId;
    public float x;
    public float y;
    public float z;
    public float bodyYaw;
    public float aimX;
    public float aimZ;
    public int lastProcessedInputTick;
}

[Serializable]
public class FireEventMessage
{
    public string type;
    public int serverTick;
    public int shooterPlayerId;
    public int requestTick;
    public float originX;
    public float originY;
    public float originZ;
    public float directionX;
    public float directionY;
    public float directionZ;
    public float range;
}

[Serializable]
public class HitEventMessage
{
    public string type;
    public int serverTick;
    public int shooterPlayerId;
    public int targetPlayerId;
    public float hitX;
    public float hitY;
    public float hitZ;
    public int damage;
}

[Serializable]
public class HealthChangedEventMessage
{
    public string type;
    public int serverTick;
    public int playerId;
    public int health;
    public int maxHealth;
    public bool isAlive;
}

public struct BufferedLocalInput
{
    public int InputTick;
    public float MoveAxis;
    public float TurnAxis;
    public float AimX;
    public float AimZ;
    public bool Fire;
    public float SentTime;
}

public struct ReconciledLocalState
{
    public Vector3 Position;
    public Quaternion Rotation;
    public float AimX;
    public float AimZ;
}
