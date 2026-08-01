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
    [SerializeField] private float smoothCorrectionMaxSeconds = 0.5f;
    [SerializeField] private bool smoothCorrectionWhileInputActive = true;
    [SerializeField] private float activeInputDeadZone = 0.05f;

    [Header("Remote Interpolation")]
    [SerializeField] private bool interpolateRemotePlayers = true;
    [SerializeField] private float remoteInterpolationDelaySeconds = 0.1f;
    [SerializeField] private int remoteInterpolationBufferSize = 8;

    [Header("Debug")]
    [SerializeField] private bool logJsonMessages = false;
    [SerializeField] private bool logSnapshots = false;
    [SerializeField] private bool logLocalPrediction = false;
    [SerializeField] private bool logGameplayEvents = true;
    [SerializeField] private bool showNetworkDebugPanel = true;
    [SerializeField] private KeyCode debugPanelToggleKey = KeyCode.F3;
    [SerializeField] private Vector2 debugPanelPosition = new Vector2(12f, 12f);
    [SerializeField] private float debugPanelWidth = 360f;

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
    private int smoothPredictionCorrectionCount;
    private int hardPredictionCorrectionCount;
    private float lastPredictionCorrectionDistance;
    private int sentFireRequestCount;
    private int receivedGameplayEventCount;
    private bool isLocalAlive = true;
    private int lastReceivedSnapshotTick;
    private int lastAppliedWorldSnapshotTick;
    private float lastSnapshotReceivedTime;
    private int receivedSnapshotCount;
    private int estimatedMissedSnapshotCount;
    private int discardedStaleSnapshotCount;
    private int collapsedSnapshotCount;
    private bool hasMeasuredRtt;
    private float smoothedRttSeconds;
    private float lastRttSampleSeconds;
    private bool lastFireUsedLagCompensation;
    private int lastLagCompensationHitTestServerTick;
    private float lastLagCompensationRewindSeconds;
    private GUIStyle debugPanelBoxStyle;
    private GUIStyle debugTitleStyle;
    private GUIStyle debugRowNameStyle;
    private GUIStyle debugRowValueStyle;
    private Vector2 debugPanelScrollPosition;

    private readonly Dictionary<int, NetworkTankAvatar> remoteAvatars = new Dictionary<int, NetworkTankAvatar>();
    private readonly HashSet<int> warnedMissingRemotePrefab = new HashSet<int>();
    private readonly List<BufferedLocalInput> localInputHistory = new List<BufferedLocalInput>();
    private readonly Queue<float> recentPredictionCorrectionTimes = new Queue<float>();

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
    public int LocalInputTick => inputTick;
    public int LastReceivedSnapshotTick => lastReceivedSnapshotTick;
    public int ReceivedSnapshotCount => receivedSnapshotCount;
    public int EstimatedMissedSnapshotCount => estimatedMissedSnapshotCount;
    public float EstimatedSnapshotLossRate => GetEstimatedSnapshotLossRate();

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
        ToggleDebugPanelIfRequested();
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

    private void OnGUI()
    {
        DrawNetworkDebugPanel();
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
        localTank.SetLocalMovementEnabled(isLocalAlive && (enableClientPrediction || !serverAuthoritativeMovement));
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

        if (!isLocalAlive)
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

        if (!isLocalAlive)
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
        message.estimatedRttSeconds = GetDisplayedRttSeconds();
        message.interpolationDelaySeconds = remoteInterpolationDelaySeconds;

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
            WorldSnapshotMessage newestSnapshot = null;
            int newestSnapshotTick = 0;

            while (udpClient.Available > 0)
            {
                IPEndPoint remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
                byte[] bytes = udpClient.Receive(ref remoteEndPoint);
                string json = Encoding.UTF8.GetString(bytes);

                if (logJsonMessages)
                {
                    Debug.Log($"UDP received: {json}");
                }

                MessageHeader header = JsonUtility.FromJson<MessageHeader>(json);

                // A WorldSnapshot represents the whole world at one server tick. If several
                // arrived during a frame hitch, only the newest one is useful for prediction.
                if (header.type == "WorldSnapshot")
                {
                    WorldSnapshotMessage snapshot = JsonUtility.FromJson<WorldSnapshotMessage>(json);

                    if (snapshot.players == null)
                    {
                        continue;
                    }

                    RecordSnapshotStats(snapshot.serverTick);

                    if (snapshot.serverTick <= lastAppliedWorldSnapshotTick)
                    {
                        discardedStaleSnapshotCount++;
                        continue;
                    }

                    if (newestSnapshot == null || snapshot.serverTick > newestSnapshotTick)
                    {
                        if (newestSnapshot != null)
                        {
                            collapsedSnapshotCount++;
                        }

                        newestSnapshot = snapshot;
                        newestSnapshotTick = snapshot.serverTick;
                    }
                    else
                    {
                        collapsedSnapshotCount++;
                    }

                    continue;
                }

                HandleMessage(json);
            }

            if (newestSnapshot != null)
            {
                ApplyWorldSnapshot(newestSnapshot);
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
        isLocalAlive = true;
        ResetLocalPredictionState();
        ResetNetworkDebugStats();

        if (localAvatar != null)
        {
            localAvatar.SetPlayerInfo(playerId, true);
        }

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

        RecordSnapshotStats(snapshot.serverTick);
        ApplyWorldSnapshot(snapshot);
    }

    private void ApplyWorldSnapshot(WorldSnapshotMessage snapshot)
    {
        if (snapshot.serverTick <= lastAppliedWorldSnapshotTick)
        {
            discardedStaleSnapshotCount++;
            return;
        }

        lastAppliedWorldSnapshotTick = snapshot.serverTick;

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
        lastFireUsedLagCompensation = fireEvent.lagCompensated;
        lastLagCompensationHitTestServerTick = fireEvent.hitTestServerTick;
        lastLagCompensationRewindSeconds = fireEvent.rewindSeconds;

        if (logGameplayEvents)
        {
            Debug.Log(
                $"FireEvent serverTick={fireEvent.serverTick}, shooter={fireEvent.shooterPlayerId}, " +
                $"lagCompensated={fireEvent.lagCompensated}, rewind={fireEvent.rewindSeconds * 1000f:F0}ms, " +
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
        avatar.SetRespawnRemainingSeconds(healthEvent.respawnRemainingSeconds);

        if (healthEvent.playerId == playerId)
        {
            SetLocalAliveFromServer(healthEvent.isAlive, null, healthEvent.serverTick);
        }

        if (logGameplayEvents)
        {
            Debug.Log(
                $"HealthChangedEvent serverTick={healthEvent.serverTick}, player={healthEvent.playerId}, " +
                $"health={healthEvent.health}/{healthEvent.maxHealth}, alive={healthEvent.isAlive}, " +
                $"respawnIn={healthEvent.respawnRemainingSeconds:F1}s");
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

        ApplySnapshotHealth(remoteAvatar, snapshot);

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
        ApplySnapshotHealth(localAvatar, snapshot);

        if (!snapshot.isAlive)
        {
            SetLocalAliveFromServer(false, snapshot, serverTick);
            StoreAuthoritativeLocalSnapshot(serverTick, snapshot);
            ApplyLocalSnapshotToTransform(snapshot);
            return;
        }
        else if (!isLocalAlive)
        {
            SetLocalAliveFromServer(true, snapshot, serverTick);
            return;
        }

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

    private void ApplySnapshotHealth(NetworkTankAvatar avatar, PlayerSnapshotMessage snapshot)
    {
        if (avatar == null)
        {
            return;
        }

        int safeMaxHealth = snapshot.maxHealth > 0 ? snapshot.maxHealth : 100;
        int safeHealth = Mathf.Clamp(snapshot.health, 0, safeMaxHealth);
        avatar.ApplyNetworkHealth(safeHealth, safeMaxHealth, snapshot.isAlive);
        avatar.SetRespawnRemainingSeconds(snapshot.respawnRemainingSeconds);
    }

    private void SetLocalAliveFromServer(bool serverSaysAlive, PlayerSnapshotMessage snapshot, int serverTick)
    {
        if (isLocalAlive == serverSaysAlive)
        {
            return;
        }

        isLocalAlive = serverSaysAlive;
        localInputHistory.Clear();
        inputTimer = 0f;

        if (!isLocalAlive)
        {
            if (localTank != null)
            {
                localTank.SetLocalMovementEnabled(false);
                localTank.SetLocalWeaponEnabled(false);
            }

            if (logGameplayEvents)
            {
                Debug.Log("Local player died. Local prediction movement and FireRequest sending are paused until respawn.");
            }

            return;
        }

        ResetLocalPredictionState();

        if (snapshot != null)
        {
            ApplyLocalSnapshotToTransform(snapshot);
            StoreAuthoritativeLocalSnapshot(serverTick, snapshot);
        }

        ApplyNetworkControlMode();

        if (logGameplayEvents)
        {
            Debug.Log("Local player respawned. Local prediction movement is enabled again.");
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
        UpdateRttFromAcknowledgedInput(lastProcessedLocalInputTick);
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
        smoothPredictionCorrectionCount = 0;
        hardPredictionCorrectionCount = 0;
        lastPredictionCorrectionDistance = 0f;
        recentPredictionCorrectionTimes.Clear();
        localInputHistory.Clear();
    }

    private void ResetNetworkDebugStats()
    {
        lastReceivedSnapshotTick = 0;
        lastAppliedWorldSnapshotTick = 0;
        lastSnapshotReceivedTime = 0f;
        receivedSnapshotCount = 0;
        estimatedMissedSnapshotCount = 0;
        discardedStaleSnapshotCount = 0;
        collapsedSnapshotCount = 0;
        hasMeasuredRtt = false;
        smoothedRttSeconds = 0f;
        lastRttSampleSeconds = 0f;
        lastFireUsedLagCompensation = false;
        lastLagCompensationHitTestServerTick = 0;
        lastLagCompensationRewindSeconds = 0f;
        sentFireRequestCount = 0;
        receivedGameplayEventCount = 0;
    }

    private void RecordSnapshotStats(int serverTick)
    {
        if (serverTick <= 0)
        {
            return;
        }

        if (lastReceivedSnapshotTick > 0 && serverTick > lastReceivedSnapshotTick + 1)
        {
            estimatedMissedSnapshotCount += serverTick - lastReceivedSnapshotTick - 1;
        }

        if (serverTick > lastReceivedSnapshotTick)
        {
            lastReceivedSnapshotTick = serverTick;
            lastSnapshotReceivedTime = Time.time;
            receivedSnapshotCount++;
        }
    }

    private void UpdateRttFromAcknowledgedInput(int acknowledgedInputTick)
    {
        if (acknowledgedInputTick <= 0)
        {
            return;
        }

        for (int i = localInputHistory.Count - 1; i >= 0; i--)
        {
            if (localInputHistory[i].InputTick > acknowledgedInputTick)
            {
                continue;
            }

            lastRttSampleSeconds = Mathf.Max(0f, Time.time - localInputHistory[i].SentTime);
            smoothedRttSeconds = hasMeasuredRtt
                ? Mathf.Lerp(smoothedRttSeconds, lastRttSampleSeconds, 0.2f)
                : lastRttSampleSeconds;
            hasMeasuredRtt = true;
            return;
        }
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
            RecordPredictionCorrection(true);
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

        RecordPredictionCorrection(false);
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

    private void RecordPredictionCorrection(bool isHardCorrection)
    {
        predictionCorrectionCount++;

        if (isHardCorrection)
        {
            hardPredictionCorrectionCount++;
        }
        else
        {
            smoothPredictionCorrectionCount++;
        }

        recentPredictionCorrectionTimes.Enqueue(Time.time);
        TrimRecentPredictionCorrections();
    }

    private int GetRecentPredictionCorrectionCount()
    {
        TrimRecentPredictionCorrections();
        return recentPredictionCorrectionTimes.Count;
    }

    private void TrimRecentPredictionCorrections()
    {
        const float recentWindowSeconds = 5f;

        while (recentPredictionCorrectionTimes.Count > 0
            && Time.time - recentPredictionCorrectionTimes.Peek() > recentWindowSeconds)
        {
            recentPredictionCorrectionTimes.Dequeue();
        }
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
                reconciledState.Rotation);
            return;
        }

        localTank.transform.position = reconciledState.Position;
        localTank.transform.rotation = reconciledState.Rotation;
    }

    private void SmoothToReconciledLocalState(ReconciledLocalState reconciledState, float stopDistance)
    {
        if (localAvatar != null)
        {
            bool includeRotation = !IsLocalTurnInputActive();

            localAvatar.SmoothToPredictedServerState(
                reconciledState.Position,
                reconciledState.Rotation,
                smoothCorrectionSpeed,
                smoothCorrectionMaxSeconds,
                stopDistance,
                includeRotation);
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

    private bool IsLocalTurnInputActive()
    {
        if (localTank == null)
        {
            return false;
        }

        return Mathf.Abs(localTank.CurrentInput.TurnAxis) > activeInputDeadZone;
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
        avatar.SetPlayerInfo(snapshot.playerId, false);
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

    private void ToggleDebugPanelIfRequested()
    {
        if (debugPanelToggleKey == KeyCode.None)
        {
            return;
        }

        if (Input.GetKeyDown(debugPanelToggleKey))
        {
            showNetworkDebugPanel = !showNetworkDebugPanel;
        }
    }

    private void DrawNetworkDebugPanel()
    {
        if (!showNetworkDebugPanel)
        {
            return;
        }

        EnsureDebugPanelStyles();

        const int debugRowCount = 16;
        const float titleHeight = 28f;
        const float rowHeight = 24f;
        const float panelPaddingHeight = 28f;

        float maxPanelWidth = Mathf.Max(280f, Screen.width - 24f);
        float safeWidth = Mathf.Clamp(debugPanelWidth, 280f, maxPanelWidth);
        float wantedHeight = titleHeight + debugRowCount * rowHeight + panelPaddingHeight;
        float maxPanelHeight = Mathf.Max(180f, Screen.height - debugPanelPosition.y - 12f);
        float safeHeight = Mathf.Min(wantedHeight, maxPanelHeight);
        Rect panelRect = new Rect(debugPanelPosition.x, debugPanelPosition.y, safeWidth, safeHeight);

        GUILayout.BeginArea(panelRect, debugPanelBoxStyle);
        debugPanelScrollPosition = GUILayout.BeginScrollView(debugPanelScrollPosition, false, true);
        GUILayout.Label($"Network Debug ({debugPanelToggleKey})", debugTitleStyle);
        DrawDebugRow("Player ID", playerId > 0 ? playerId.ToString() : "waiting");
        DrawDebugRow("Connection", GetConnectionStateText());
        DrawDebugRow("RTT", FormatRttText());
        DrawDebugRow("World ticks", FormatWorldTickText());
        DrawDebugRow("Input ticks", FormatInputTickText());
        DrawDebugRow("Snapshot buffer", $"{GetTotalRemoteSnapshotBufferCount()} remote snapshots");
        DrawDebugRow("Smooth corrections", smoothPredictionCorrectionCount.ToString());
        DrawDebugRow("Hard corrections", hardPredictionCorrectionCount.ToString());
        DrawDebugRow("Corrections (5s)", GetRecentPredictionCorrectionCount().ToString());
        DrawDebugRow("Latest prediction error", $"{lastPredictionCorrectionDistance:F3} m");
        DrawDebugRow("Snapshot delivery", FormatSnapshotDeliveryText());
        DrawDebugRow("Snapshot processing", FormatSkippedSnapshotText());
        DrawDebugRow("Fire lag compensation", FormatLagCompensationText());
        DrawDebugRow("Correction budget", FormatCorrectionBudgetText());
        DrawDebugRow("Smooth correction", $"{smoothCorrectionSpeed:F1}/s, max {smoothCorrectionMaxSeconds:F2}s");
        DrawDebugRow("Remote interpolation", $"{remoteInterpolationDelaySeconds:F3}s ({GetRemoteInterpolationDelayTicks()} ticks)");
        GUILayout.EndScrollView();
        GUILayout.EndArea();
    }

    private void EnsureDebugPanelStyles()
    {
        if (debugPanelBoxStyle != null)
        {
            return;
        }

        debugPanelBoxStyle = new GUIStyle(GUI.skin.box);
        debugPanelBoxStyle.alignment = TextAnchor.UpperLeft;
        debugPanelBoxStyle.padding = new RectOffset(12, 12, 10, 10);

        debugTitleStyle = new GUIStyle(GUI.skin.label);
        debugTitleStyle.fontStyle = FontStyle.Bold;
        debugTitleStyle.fontSize = 15;
        debugTitleStyle.normal.textColor = Color.white;

        debugRowNameStyle = new GUIStyle(GUI.skin.label);
        debugRowNameStyle.normal.textColor = new Color(0.8f, 0.9f, 1f);

        debugRowValueStyle = new GUIStyle(GUI.skin.label);
        debugRowValueStyle.alignment = TextAnchor.MiddleRight;
        debugRowValueStyle.wordWrap = true;
        debugRowValueStyle.normal.textColor = Color.white;
    }

    private void DrawDebugRow(string name, string value)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(name, debugRowNameStyle, GUILayout.Width(165f));
        GUILayout.Label(value, debugRowValueStyle);
        GUILayout.EndHorizontal();
    }

    private string GetConnectionStateText()
    {
        if (udpClient == null)
        {
            return "Socket closed";
        }

        if (playerId == 0)
        {
            return "Socket open, waiting welcome";
        }

        if (lastReceivedSnapshotTick == 0)
        {
            return "Welcome received, waiting snapshot";
        }

        float secondsSinceSnapshot = Time.time - lastSnapshotReceivedTime;

        if (secondsSinceSnapshot > 1.5f)
        {
            return $"Connected, snapshot stale {secondsSinceSnapshot:F1}s";
        }

        return isLocalAlive ? "Connected, alive" : "Connected, dead";
    }

    private string FormatRttText()
    {
        if (hasMeasuredRtt)
        {
            return $"{FormatSecondsAsMilliseconds(smoothedRttSeconds)} (last {FormatSecondsAsMilliseconds(lastRttSampleSeconds)})";
        }

        if (playerId == 0 || !hasAuthoritativeLocalSnapshot)
        {
            return "--";
        }

        return $"{FormatSecondsAsMilliseconds(EstimateLocalRttSeconds())} estimated";
    }

    private float GetDisplayedRttSeconds()
    {
        if (hasMeasuredRtt)
        {
            return smoothedRttSeconds;
        }

        if (playerId == 0 || !hasAuthoritativeLocalSnapshot)
        {
            return 0f;
        }

        return EstimateLocalRttSeconds();
    }

    private int GetEstimatedServerTick()
    {
        if (lastReceivedSnapshotTick <= 0)
        {
            return 0;
        }

        float secondsSinceSnapshot = Mathf.Max(0f, Time.time - lastSnapshotReceivedTime);
        int ticksSinceSnapshot = Mathf.RoundToInt(secondsSinceSnapshot * Mathf.Max(1f, inputTickRate));
        return lastReceivedSnapshotTick + ticksSinceSnapshot;
    }

    private string FormatWorldTickText()
    {
        if (lastReceivedSnapshotTick <= 0)
        {
            return "waiting for snapshot";
        }

        return $"server {GetEstimatedServerTick()}, snapshot {lastReceivedSnapshotTick}";
    }

    private string FormatInputTickText()
    {
        return $"local {inputTick}, ack {lastProcessedLocalInputTick}";
    }

    private string FormatCorrectionBudgetText()
    {
        return $"soft {GetAdjustedDeadZone():F2}m, hard {GetAdjustedHardCorrectionDistance():F2}m";
    }

    private int GetTotalRemoteSnapshotBufferCount()
    {
        int total = 0;

        foreach (NetworkTankAvatar remoteAvatar in remoteAvatars.Values)
        {
            if (remoteAvatar != null)
            {
                total += remoteAvatar.RemoteSnapshotBufferCount;
            }
        }

        return total;
    }

    private float GetEstimatedSnapshotLossRate()
    {
        int expectedSnapshots = receivedSnapshotCount + estimatedMissedSnapshotCount;

        if (expectedSnapshots <= 0)
        {
            return 0f;
        }

        return estimatedMissedSnapshotCount / (float)expectedSnapshots;
    }

    private string FormatSnapshotDeliveryText()
    {
        float lossPercent = GetEstimatedSnapshotLossRate() * 100f;
        return $"{lossPercent:F1}% loss, {estimatedMissedSnapshotCount}/{receivedSnapshotCount} missed";
    }

    private string FormatSkippedSnapshotText()
    {
        return $"{collapsedSnapshotCount} batched, {discardedStaleSnapshotCount} stale";
    }

    private string FormatLagCompensationText()
    {
        if (lastLagCompensationHitTestServerTick <= 0)
        {
            return "--";
        }

        string enabledText = lastFireUsedLagCompensation ? "on" : "off";
        return $"{enabledText}, rewind {lastLagCompensationRewindSeconds * 1000f:F0} ms, tick {lastLagCompensationHitTestServerTick}";
    }

    private string FormatSecondsAsMilliseconds(float seconds)
    {
        return $"{seconds * 1000f:F0} ms";
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
    public float estimatedRttSeconds;
    public float interpolationDelaySeconds;
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
    public int health;
    public int maxHealth;
    public bool isAlive;
    public float respawnRemainingSeconds;
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
    public bool lagCompensated;
    public int hitTestServerTick;
    public float rewindSeconds;
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
    public float respawnRemainingSeconds;
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
