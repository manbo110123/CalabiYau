using System.Text.Json;
using CalabiYau.TankCollision;

internal static class CollisionMapHandshakeTests
{
    public static void RunAll()
    {
        ClientHelloCarriesRequiredCollisionVersion();
        WelcomeEchoesAuthoritativeCollisionVersion();
        MismatchRejectionIsExplicitlySerializable();
    }

    private static void ClientHelloCarriesRequiredCollisionVersion()
    {
        ClientHelloMessage hello = new ClientHelloMessage
        {
            Type = "ClientHello",
            Name = "Test",
            MapId = TrainingCollisionMap2D.MapId,
            CollisionRevision = TrainingCollisionMap2D.CollisionRevision
        };
        string json = JsonSerializer.Serialize(hello);

        CollisionCoreTests.Assert(
            json.Contains($"\"mapId\":\"{TrainingCollisionMap2D.MapId}\""),
            "ClientHello JSON must carry mapId");
        CollisionCoreTests.Assert(
            json.Contains($"\"collisionRevision\":{TrainingCollisionMap2D.CollisionRevision}"),
            "ClientHello JSON must carry collisionRevision");
    }

    private static void WelcomeEchoesAuthoritativeCollisionVersion()
    {
        ServerWelcomeMessage welcome = new ServerWelcomeMessage
        {
            Type = "ServerWelcome",
            PlayerId = 1,
            ServerTickRate = 30,
            MapId = TrainingCollisionMap2D.MapId,
            CollisionRevision = TrainingCollisionMap2D.CollisionRevision,
            Message = "ok"
        };
        ServerWelcomeMessage? roundTrip = JsonSerializer.Deserialize<ServerWelcomeMessage>(
            JsonSerializer.Serialize(welcome));

        CollisionCoreTests.Assert(roundTrip != null, "ServerWelcome JSON must deserialize");
        CollisionCoreTests.Assert(
            TrainingCollisionMap2D.IsCompatible(roundTrip!.MapId, roundTrip.CollisionRevision),
            "ServerWelcome round trip must preserve the authoritative map version");
    }

    private static void MismatchRejectionIsExplicitlySerializable()
    {
        ServerRejectMessage rejection = new ServerRejectMessage
        {
            Type = "ServerReject",
            Reason = "collision-map-mismatch",
            ExpectedMapId = TrainingCollisionMap2D.MapId,
            ExpectedCollisionRevision = TrainingCollisionMap2D.CollisionRevision
        };
        string json = JsonSerializer.Serialize(rejection);

        CollisionCoreTests.Assert(
            json.Contains("\"type\":\"ServerReject\""),
            "mismatch rejection must have a routable message type");
        CollisionCoreTests.Assert(
            json.Contains("\"reason\":\"collision-map-mismatch\""),
            "mismatch rejection must explain the collision version failure");
    }
}
