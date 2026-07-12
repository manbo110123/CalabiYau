using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

const int listenPort = 7777;

using UdpClient udpServer = new UdpClient(listenPort);
Dictionary<string, ConnectedClient> clients = new Dictionary<string, ConnectedClient>();
int nextPlayerId = 1;

Console.WriteLine($"UDP server started on port {listenPort}.");
Console.WriteLine("Waiting for Unity ClientHello messages...");

while (true)
{
    UdpReceiveResult received = await udpServer.ReceiveAsync();
    string json = Encoding.UTF8.GetString(received.Buffer);
    string clientKey = GetClientKey(received.RemoteEndPoint);

    Console.WriteLine();
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] From {clientKey}");
    Console.WriteLine(json);

    string messageType = ReadMessageType(json);

    switch (messageType)
    {
        case "ClientHello":
            await HandleClientHello(udpServer, clients, received.RemoteEndPoint, json);
            break;

        case "":
            Console.WriteLine("Message ignored: JSON is missing a readable type field.");
            break;

        default:
            Console.WriteLine($"Message ignored: unsupported type '{messageType}'.");
            break;
    }
}

async Task HandleClientHello(
    UdpClient udpServer,
    Dictionary<string, ConnectedClient> clients,
    IPEndPoint remoteEndPoint,
    string json)
{
    string clientKey = GetClientKey(remoteEndPoint);
    ClientHelloMessage? hello = JsonSerializer.Deserialize<ClientHelloMessage>(json);
    string playerName = string.IsNullOrWhiteSpace(hello?.Name) ? "Player" : hello.Name;

    if (!clients.TryGetValue(clientKey, out ConnectedClient? client))
    {
        client = new ConnectedClient(nextPlayerId, playerName, remoteEndPoint);
        clients.Add(clientKey, client);
        nextPlayerId++;

        Console.WriteLine($"New client connected: {playerName}, playerId={client.PlayerId}");
    }
    else
    {
        client.Name = playerName;
        client.RemoteEndPoint = remoteEndPoint;
        Console.WriteLine($"Known client said hello again: {playerName}, playerId={client.PlayerId}");
    }

    ServerWelcomeMessage welcome = new ServerWelcomeMessage
    {
        Type = "ServerWelcome",
        PlayerId = client.PlayerId,
        Message = "Welcome to the UDP demo server."
    };

    string replyJson = JsonSerializer.Serialize(welcome);
    byte[] replyBytes = Encoding.UTF8.GetBytes(replyJson);
    await udpServer.SendAsync(replyBytes, replyBytes.Length, remoteEndPoint);

    Console.WriteLine($"Sent to {clientKey}");
    Console.WriteLine(replyJson);
}

string ReadMessageType(string json)
{
    try
    {
        using JsonDocument document = JsonDocument.Parse(json);

        if (document.RootElement.TryGetProperty("type", out JsonElement typeElement))
        {
            return typeElement.GetString() ?? string.Empty;
        }
    }
    catch (JsonException exception)
    {
        Console.WriteLine($"Invalid JSON: {exception.Message}");
    }

    return string.Empty;
}

string GetClientKey(IPEndPoint endPoint)
{
    return $"{endPoint.Address}:{endPoint.Port}";
}

public sealed class ClientHelloMessage
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
}

public sealed class ServerWelcomeMessage
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("playerId")]
    public int PlayerId { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;
}

public sealed class ConnectedClient
{
    public ConnectedClient(int playerId, string name, IPEndPoint remoteEndPoint)
    {
        PlayerId = playerId;
        Name = name;
        RemoteEndPoint = remoteEndPoint;
    }

    public int PlayerId { get; }
    public string Name { get; set; }
    public IPEndPoint RemoteEndPoint { get; set; }
}
