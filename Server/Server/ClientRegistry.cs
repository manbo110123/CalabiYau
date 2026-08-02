using System.Net;

public sealed class ClientRegistry
{
    private readonly Dictionary<string, ConnectedClient> clientsByEndPoint = new Dictionary<string, ConnectedClient>();
    private int nextPlayerId = 1;

    public int Count => clientsByEndPoint.Count;

    public ClientRegistration RegisterOrUpdate(string playerName, IPEndPoint remoteEndPoint, ClientReplicationSettings replicationSettings)
    {
        string clientKey = GetClientKey(remoteEndPoint);

        if (!clientsByEndPoint.TryGetValue(clientKey, out ConnectedClient? client))
        {
            client = new ConnectedClient(nextPlayerId, playerName, remoteEndPoint, new ClientReplicator(replicationSettings));
            nextPlayerId++;
            clientsByEndPoint.Add(clientKey, client);
            return new ClientRegistration(client, true);
        }

        client.Name = playerName;
        client.RemoteEndPoint = remoteEndPoint;
        client.LastReceivedUtc = DateTime.UtcNow;
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

    public bool Touch(IPEndPoint remoteEndPoint, DateTime receivedAtUtc)
    {
        if (!clientsByEndPoint.TryGetValue(GetClientKey(remoteEndPoint), out ConnectedClient? client))
        {
            return false;
        }

        client.LastReceivedUtc = receivedAtUtc;
        return true;
    }

    public bool TryRemove(IPEndPoint remoteEndPoint, out ConnectedClient? client)
    {
        string clientKey = GetClientKey(remoteEndPoint);

        if (!clientsByEndPoint.TryGetValue(clientKey, out client))
        {
            return false;
        }

        clientsByEndPoint.Remove(clientKey);
        return true;
    }

    public List<ConnectedClient> RemoveInactive(DateTime nowUtc, TimeSpan timeout)
    {
        List<ConnectedClient> removedClients = new List<ConnectedClient>();

        foreach (KeyValuePair<string, ConnectedClient> pair in clientsByEndPoint.ToArray())
        {
            if (nowUtc - pair.Value.LastReceivedUtc < timeout)
            {
                continue;
            }

            clientsByEndPoint.Remove(pair.Key);
            removedClients.Add(pair.Value);
        }

        return removedClients;
    }

    public List<ConnectedClient> GetAllClients()
    {
        return clientsByEndPoint.Values.ToList();
    }

    public static string GetClientKey(IPEndPoint endPoint)
    {
        return $"{endPoint.Address}:{endPoint.Port}";
    }
}

public sealed class ConnectedClient
{
    public ConnectedClient(int playerId, string name, IPEndPoint remoteEndPoint, ClientReplicator replicator)
    {
        PlayerId = playerId;
        Name = name;
        RemoteEndPoint = remoteEndPoint;
        Replicator = replicator;
        LastReceivedUtc = DateTime.UtcNow;
    }

    public int PlayerId { get; }
    public string Name { get; set; }
    public IPEndPoint RemoteEndPoint { get; set; }
    public ClientReplicator Replicator { get; }
    public DateTime LastReceivedUtc { get; set; }
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
