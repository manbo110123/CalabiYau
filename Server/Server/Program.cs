using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

const int listenPort = 7777;
const int serverTickRate = 30;
const float tickDeltaTime = 1f / serverTickRate;

// These are server-side authority values. Clients may change their local
// movement settings, but the final networked position is calculated here.
const float playerMoveSpeed = 7f;
const float playerTurnDegreesPerSecond = 180f;

using UdpClient udpServer = new UdpClient(listenPort);
object stateLock = new object();

Dictionary<string, ConnectedClient> clientsByEndPoint = new Dictionary<string, ConnectedClient>();
Dictionary<int, PlayerState> playersById = new Dictionary<int, PlayerState>();

int nextPlayerId = 1;
int serverTick = 0;
long receivedInputCount = 0;
long sentSnapshotCount = 0;

Console.WriteLine($"UDP server started on port {listenPort}.");
Console.WriteLine($"Server authority tick rate: {serverTickRate} Hz.");
Console.WriteLine("Waiting for Unity ClientHello and PlayerInput messages...");

Task receiveTask = ReceiveLoopAsync();
Task tickTask = TickLoopAsync();

await Task.WhenAll(receiveTask, tickTask);

async Task ReceiveLoopAsync()
{
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
                await HandleClientHello(received.RemoteEndPoint, json);
                break;

            case "PlayerInput":
                HandlePlayerInput(received.RemoteEndPoint, json);
                break;

            case "":
                Console.WriteLine("Message ignored: JSON is missing a readable type field.");
                break;

            default:
                Console.WriteLine($"Message ignored: unsupported type '{messageType}'.");
                break;
        }
    }
}

async Task TickLoopAsync()
{
    using PeriodicTimer timer = new PeriodicTimer(TimeSpan.FromSeconds(tickDeltaTime));

    while (await timer.WaitForNextTickAsync())
    {
        WorldSnapshotMessage snapshot;
        List<IPEndPoint> targets;

        lock (stateLock)
        {
            serverTick++;

            foreach (PlayerState player in playersById.Values)
            {
                SimulatePlayer(player, tickDeltaTime);
            }

            snapshot = BuildWorldSnapshot();
            targets = clientsByEndPoint.Values
                .Select(client => client.RemoteEndPoint)
                .ToList();
        }

        if (targets.Count > 0)
        {
            await BroadcastSnapshot(snapshot, targets);
        }
    }
}

async Task HandleClientHello(IPEndPoint remoteEndPoint, string json)
{
    ClientHelloMessage? hello = JsonSerializer.Deserialize<ClientHelloMessage>(json);
    string playerName = string.IsNullOrWhiteSpace(hello?.Name) ? "Player" : hello.Name;
    string clientKey = GetClientKey(remoteEndPoint);

    ConnectedClient client;

    lock (stateLock)
    {
        if (!clientsByEndPoint.TryGetValue(clientKey, out client!))
        {
            int playerId = nextPlayerId;
            nextPlayerId++;

            client = new ConnectedClient(playerId, playerName, remoteEndPoint);
            clientsByEndPoint.Add(clientKey, client);

            PlayerState player = CreateInitialPlayerState(playerId);
            playersById.Add(playerId, player);

            Console.WriteLine($"New client connected: {playerName}, playerId={playerId}");
        }
        else
        {
            client.Name = playerName;
            client.RemoteEndPoint = remoteEndPoint;
            Console.WriteLine($"Known client said hello again: {playerName}, playerId={client.PlayerId}");
        }
    }

    ServerWelcomeMessage welcome = new ServerWelcomeMessage
    {
        Type = "ServerWelcome",
        PlayerId = client.PlayerId,
        Message = "Welcome to the UDP demo server."
    };

    await SendJson(welcome, remoteEndPoint);
}

void HandlePlayerInput(IPEndPoint remoteEndPoint, string json)
{
    PlayerInputMessage? input;

    try
    {
        input = JsonSerializer.Deserialize<PlayerInputMessage>(json);
    }
    catch (JsonException exception)
    {
        Console.WriteLine($"PlayerInput ignored: invalid JSON. {exception.Message}");
        return;
    }

    if (input == null)
    {
        Console.WriteLine("PlayerInput ignored: empty message.");
        return;
    }

    string clientKey = GetClientKey(remoteEndPoint);

    lock (stateLock)
    {
        if (!clientsByEndPoint.TryGetValue(clientKey, out ConnectedClient? client))
        {
            Console.WriteLine("PlayerInput ignored: sender has not completed ClientHello.");
            return;
        }

        if (client.PlayerId != input.PlayerId)
        {
            Console.WriteLine($"PlayerInput ignored: endpoint owns playerId={client.PlayerId}, not {input.PlayerId}.");
            return;
        }

        if (!playersById.TryGetValue(input.PlayerId, out PlayerState? player))
        {
            Console.WriteLine($"PlayerInput ignored: unknown playerId={input.PlayerId}.");
            return;
        }

        player.LatestInput = input;
        player.LastProcessedInputTick = input.InputTick;
        receivedInputCount++;
    }
}

void SimulatePlayer(PlayerState player, float deltaTime)
{
    PlayerInputMessage input = player.LatestInput;

    float moveAxis = Clamp(input.MoveAxis, -1f, 1f);
    float turnAxis = Clamp(input.TurnAxis, -1f, 1f);

    player.BodyYaw += turnAxis * playerTurnDegreesPerSecond * deltaTime;

    float yawRadians = DegreesToRadians(player.BodyYaw);
    float forwardX = MathF.Sin(yawRadians);
    float forwardZ = MathF.Cos(yawRadians);

    player.X += forwardX * playerMoveSpeed * moveAxis * deltaTime;
    player.Z += forwardZ * playerMoveSpeed * moveAxis * deltaTime;
    player.AimX = input.AimX;
    player.AimZ = input.AimZ;
}

WorldSnapshotMessage BuildWorldSnapshot()
{
    PlayerSnapshotMessage[] playerSnapshots = playersById.Values
        .OrderBy(player => player.PlayerId)
        .Select(player => new PlayerSnapshotMessage
        {
            PlayerId = player.PlayerId,
            X = player.X,
            Y = player.Y,
            Z = player.Z,
            BodyYaw = player.BodyYaw,
            AimX = player.AimX,
            AimZ = player.AimZ,
            LastProcessedInputTick = player.LastProcessedInputTick
        })
        .ToArray();

    return new WorldSnapshotMessage
    {
        Type = "WorldSnapshot",
        ServerTick = serverTick,
        Players = playerSnapshots
    };
}

async Task BroadcastSnapshot(WorldSnapshotMessage snapshot, List<IPEndPoint> targets)
{
    string json = JsonSerializer.Serialize(snapshot);
    byte[] bytes = Encoding.UTF8.GetBytes(json);

    foreach (IPEndPoint target in targets)
    {
        await udpServer.SendAsync(bytes, bytes.Length, target);
    }

    sentSnapshotCount++;

    if (serverTick % serverTickRate == 0)
    {
        Console.WriteLine(
            $"Tick={serverTick}, players={snapshot.Players.Length}, " +
            $"inputs={receivedInputCount}, snapshots={sentSnapshotCount}");
    }
}

async Task SendJson<TMessage>(TMessage message, IPEndPoint target)
{
    string json = JsonSerializer.Serialize(message);
    byte[] bytes = Encoding.UTF8.GetBytes(json);
    await udpServer.SendAsync(bytes, bytes.Length, target);

    Console.WriteLine($"Sent to {GetClientKey(target)}");
    Console.WriteLine(json);
}

PlayerState CreateInitialPlayerState(int playerId)
{
    float spawnX = (playerId - 1) * 4f;

    PlayerState player = new PlayerState();
    player.PlayerId = playerId;
    player.X = spawnX;
    player.Y = 0f;
    player.Z = 0f;
    player.BodyYaw = 0f;
    player.AimX = spawnX;
    player.AimZ = 5f;
    player.LatestInput = new PlayerInputMessage
    {
        Type = "PlayerInput",
        PlayerId = playerId,
        InputTick = 0,
        MoveAxis = 0f,
        TurnAxis = 0f,
        AimX = spawnX,
        AimZ = 5f,
        Fire = false
    };

    return player;
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

float Clamp(float value, float min, float max)
{
    if (value < min)
    {
        return min;
    }

    if (value > max)
    {
        return max;
    }

    return value;
}

float DegreesToRadians(float degrees)
{
    return degrees * MathF.PI / 180f;
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

public sealed class PlayerInputMessage
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("playerId")]
    public int PlayerId { get; set; }

    [JsonPropertyName("inputTick")]
    public int InputTick { get; set; }

    [JsonPropertyName("moveAxis")]
    public float MoveAxis { get; set; }

    [JsonPropertyName("turnAxis")]
    public float TurnAxis { get; set; }

    [JsonPropertyName("aimX")]
    public float AimX { get; set; }

    [JsonPropertyName("aimZ")]
    public float AimZ { get; set; }

    [JsonPropertyName("fire")]
    public bool Fire { get; set; }
}

public sealed class WorldSnapshotMessage
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("serverTick")]
    public int ServerTick { get; set; }

    [JsonPropertyName("players")]
    public PlayerSnapshotMessage[] Players { get; set; } = Array.Empty<PlayerSnapshotMessage>();
}

public sealed class PlayerSnapshotMessage
{
    [JsonPropertyName("playerId")]
    public int PlayerId { get; set; }

    [JsonPropertyName("x")]
    public float X { get; set; }

    [JsonPropertyName("y")]
    public float Y { get; set; }

    [JsonPropertyName("z")]
    public float Z { get; set; }

    [JsonPropertyName("bodyYaw")]
    public float BodyYaw { get; set; }

    [JsonPropertyName("aimX")]
    public float AimX { get; set; }

    [JsonPropertyName("aimZ")]
    public float AimZ { get; set; }

    [JsonPropertyName("lastProcessedInputTick")]
    public int LastProcessedInputTick { get; set; }
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

public sealed class PlayerState
{
    public int PlayerId { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public float BodyYaw { get; set; }
    public float AimX { get; set; }
    public float AimZ { get; set; }
    public int LastProcessedInputTick { get; set; }
    public PlayerInputMessage LatestInput { get; set; } = new PlayerInputMessage();
}
