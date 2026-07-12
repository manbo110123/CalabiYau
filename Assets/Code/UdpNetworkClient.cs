using System;
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

    [Header("Debug")]
    [SerializeField] private bool logJsonMessages = true;

    private UdpClient udpClient;
    private int playerId;

    public int PlayerId => playerId;

    private void Start()
    {
        OpenSocket();

        if (sendHelloOnStart)
        {
            SendClientHello();
        }
    }

    private void Update()
    {
        ReceivePendingMessages();
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

            default:
                Debug.LogWarning($"UDP message ignored: unsupported type '{header.type}'.");
                break;
        }
    }

    private void HandleServerWelcome(string json)
    {
        ServerWelcomeMessage welcome = JsonUtility.FromJson<ServerWelcomeMessage>(json);
        playerId = welcome.playerId;

        Debug.Log($"Connected to server. playerId={playerId}, message={welcome.message}");
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
