using System.Net;

public sealed class ClientRegistry
{
    private readonly Dictionary<string, ConnectedClient> clientsByEndPoint = new Dictionary<string, ConnectedClient>();
    private int nextPlayerId = 1;

    public int Count => clientsByEndPoint.Count;

    public ClientRegistration RegisterOrUpdate(string playerName, IPEndPoint remoteEndPoint)
    {
        string clientKey = GetClientKey(remoteEndPoint);

        if (!clientsByEndPoint.TryGetValue(clientKey, out ConnectedClient? client))
        {
            client = new ConnectedClient(nextPlayerId, playerName, remoteEndPoint);
            nextPlayerId++;
            clientsByEndPoint.Add(clientKey, client);
            return new ClientRegistration(client, true);
        }

        client.Name = playerName;
        client.RemoteEndPoint = remoteEndPoint;
        return new ClientRegistration(client, false);
    }

    public bool TryGetClient(IPEndPoint remoteEndPoint, out ConnectedClient? client)
    {
        return clientsByEndPoint.TryGetValue(GetClientKey(remoteEndPoint), out client);
    }

    public List<IPEndPoint> GetAllEndpoints()
    {
        return clientsByEndPoint.Values
            .Select(client => client.RemoteEndPoint)
            .ToList();
    }

    public static string GetClientKey(IPEndPoint endPoint)
    {
        return $"{endPoint.Address}:{endPoint.Port}";
    }
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

public readonly struct ClientRegistration
{
    public ClientRegistration(ConnectedClient client, bool isNewClient)
    {
        Client = client;
        IsNewClient = isNewClient;
    }

    public ConnectedClient Client { get; }
    public bool IsNewClient { get; }
}
