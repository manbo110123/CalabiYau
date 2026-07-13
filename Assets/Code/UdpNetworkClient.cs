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
    [SerializeField] private bool disableLocalWeaponInNetworkMode = true;

    [Header("Remote Players")]
    [SerializeField] private GameObject remoteTankPrefab;
    [SerializeField] private Transform remoteTankParent;

    [Header("Network Tick")]
    [SerializeField] private float inputTickRate = 30f;

    [Header("Debug")]
    [SerializeField] private bool logJsonMessages = true;
    [SerializeField] private bool logSnapshots = false;

    private UdpClient udpClient;
    private int playerId;
    private int inputTick;
    private float inputTimer;

    private readonly Dictionary<int, NetworkTankAvatar> remoteAvatars = new Dictionary<int, NetworkTankAvatar>();
    private readonly HashSet<int> warnedMissingRemotePrefab = new HashSet<int>();

    public int PlayerId => playerId;

    private void Start()
    {
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

        if (localAvatar == null && localTank != null)
        {
            localAvatar = localTank.GetComponent<NetworkTankAvatar>();
        }

        if (localAvatar == null && localTank != null)
        {
            localAvatar = localTank.gameObject.AddComponent<NetworkTankAvatar>();
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

    private void ApplyNetworkControlMode()
    {
        if (localTank == null || !serverAuthoritativeMovement)
        {
            return;
        }

        localTank.SetLocalMovementEnabled(false);

        if (localAvatar != null)
        {
            localAvatar.SetNetworkAuthorityMode(true);
        }

        if (disableLocalWeaponInNetworkMode)
        {
            localTank.SetLocalWeaponEnabled(false);
        }
    }

    private void ApplyOfflineControlMode()
    {
        if (localTank != null)
        {
            localTank.SetLocalControlEnabled(true);
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

        SendJson(JsonUtility.ToJson(message));
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

            default:
                Debug.LogWarning($"UDP message ignored: unsupported type '{header.type}'.");
                break;
        }
    }

    private void HandleServerWelcome(string json)
    {
        ServerWelcomeMessage welcome = JsonUtility.FromJson<ServerWelcomeMessage>(json);
        playerId = welcome.playerId;
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
            ApplyPlayerSnapshot(snapshot.players[i]);
        }
    }

    private void ApplyPlayerSnapshot(PlayerSnapshotMessage snapshot)
    {
        if (snapshot.playerId == playerId)
        {
            ApplyLocalSnapshot(snapshot);
            return;
        }

        NetworkTankAvatar remoteAvatar = GetOrCreateRemoteAvatar(snapshot);

        if (remoteAvatar == null)
        {
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

    private void ApplyLocalSnapshot(PlayerSnapshotMessage snapshot)
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
        remoteAvatars.Add(snapshot.playerId, avatar);
        return avatar;
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
