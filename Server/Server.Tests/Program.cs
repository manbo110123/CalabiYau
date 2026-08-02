using System.Net;

internal static class Program
{
    private static void Main()
    {
        DuplicateAndOutOfOrderInputsDoNotMoveTheWorldBackward();
        InvalidAndFutureInputsAreRejectedAtTheWorldGate();
        DeadPlayersCannotQueueInputsOrFireRequests();
        PerClientReplicationAlwaysSendsSelfAndFiltersOutOfRangePlayers();
        LowPriorityEntitiesKeepTheirScopeButUseALowerStateRate();
        FullSnapshotModePreservesTheChapterOneBaseline();
        DistanceFilteringIsTheDefaultReplicationMode();
        InactiveClientsAreRemovedFromTheRegistry();

        Console.WriteLine("GameWorld command timeline and stage-11 replication checks passed.");
    }

    private static void DuplicateAndOutOfOrderInputsDoNotMoveTheWorldBackward()
    {
        GameWorld world = CreateWorld();
        Assert(world.AddPlayer(1), "player 1 should be added");

        AssertAccepted(world.TryQueueInput(1, Input(1, 1f)), "input tick 1");
        world.Tick(1f / 30f);
        PlayerState player = world.Players.Single();
        float positionAfterTickOne = player.Z;

        AssertAccepted(world.TryQueueInput(1, Input(3, 1f)), "input tick 3");
        AssertAccepted(world.TryQueueInput(1, Input(2, -1f)), "late input tick 2 while tick 3 is buffered");
        world.Tick(1f / 30f);

        Assert(player.LastProcessedInputTick == 3, "the newest buffered tick should be consumed");
        Assert(player.Z > positionAfterTickOne, "late tick 2 must not move the player backward");
        AssertRejected(world.TryQueueInput(1, Input(2, -1f)), "already processed tick 2");
        Assert(world.SupersededInputCount == 1, "one older buffered command should be superseded");
    }

    private static void InvalidAndFutureInputsAreRejectedAtTheWorldGate()
    {
        GameWorld world = CreateWorld();
        Assert(world.AddPlayer(1), "player 1 should be added");

        AssertRejected(world.TryQueueInput(1, Input(1, 2f)), "out-of-range movement axis");
        AssertRejected(world.TryQueueInput(1, new InputCommand(1, 0f, 0f, float.NaN, 5f, false)), "NaN aim value");
        AssertRejected(world.TryQueueInput(1, Input(31, 0f)), "input outside the future window");
        Assert(world.RejectedInputCount == 3, "all invalid inputs must be counted");
        Assert(world.InputRejectionsByReason.ContainsKey("movement-axis-out-of-range"), "axis rejection reason should be observable");
        Assert(world.InputRejectionsByReason.ContainsKey("non-finite-input-value"), "NaN rejection reason should be observable");
        Assert(world.InputRejectionsByReason.ContainsKey("input-too-far-in-future"), "future rejection reason should be observable");
    }

    private static void DeadPlayersCannotQueueInputsOrFireRequests()
    {
        GameWorld world = new GameWorld(new GameWorldSettings
        {
            FireDamage = 100,
            FireCooldownSeconds = 0f,
            AimToleranceMeters = 50f
        });
        Assert(world.AddPlayer(1), "player 1 should be added");
        Assert(world.AddPlayer(2), "player 2 should be added");

        bool shotAccepted = world.TryHandleFireRequest(1, Fire(1, 4f, 0f, 1f, 0f, 0f), out _, out string shotRejectReason);
        Assert(shotAccepted, $"killing shot should be accepted: {shotRejectReason}");
        PlayerState playerTwo = world.Players.Single(player => player.PlayerId == 2);
        Assert(!playerTwo.IsAlive, "player 2 should be dead after the authoritative hit");

        AssertRejected(world.TryQueueInput(2, Input(1, 1f)), "input from a dead player");
        bool deadFireAccepted = world.TryHandleFireRequest(2, Fire(1, 0f, 0f, -1f, 0f, 0f), out _, out string deadFireRejectReason);
        Assert(!deadFireAccepted && deadFireRejectReason.Contains("dead"), "fire request from a dead player must be rejected");
        Assert(world.FireRejectionsByReason.ContainsKey("player-dead"), "dead fire rejection should be observable");
    }

    private static void PerClientReplicationAlwaysSendsSelfAndFiltersOutOfRangePlayers()
    {
        GameWorld world = CreateWorldWithThreePlayers();
        SnapshotBuilder builder = new SnapshotBuilder();
        ClientReplicator replicator = new ClientReplicator(new ClientReplicationSettings
        {
            EnableDistanceFiltering = true,
            HighPriorityDistanceMeters = 5f,
            ReplicationDistanceMeters = 5f,
            CombatPriorityDurationTicks = 0
        });

        ClientSnapshotPlan plan = replicator.BuildSnapshot(1, 1, 30, builder.Capture(world));

        Assert(plan.Snapshot.IsFullState, "phase-11 snapshots must use the full-state fallback before baseline acknowledgement exists");
        Assert(plan.Snapshot.Players.Select(player => player.PlayerId).SequenceEqual(new[] { 1, 2 }), "self and nearby player should have state updates");
        Assert(plan.Snapshot.ReplicatedPlayerIds.SequenceEqual(new[] { 1, 2 }), "out-of-range player must leave this client's replication scope");
        Assert(plan.Snapshot.Players.All(player => player.ChangeMask == SnapshotChangeMasks.All), "full fallback must mark every serialized field as available");
    }

    private static void LowPriorityEntitiesKeepTheirScopeButUseALowerStateRate()
    {
        GameWorld world = CreateWorldWithThreePlayers();
        SnapshotBuilder builder = new SnapshotBuilder();
        ClientReplicator replicator = new ClientReplicator(new ClientReplicationSettings
        {
            EnableDistanceFiltering = true,
            HighPriorityDistanceMeters = 3f,
            ReplicationDistanceMeters = 5f,
            LowPrioritySnapshotRate = 5,
            CombatPriorityDurationTicks = 0
        });

        ClientSnapshotPlan firstPlan = replicator.BuildSnapshot(1, 1, 30, builder.Capture(world));
        ClientSnapshotPlan secondPlan = replicator.BuildSnapshot(1, 2, 30, builder.Capture(world));
        ClientSnapshotPlan sixthTickPlan = replicator.BuildSnapshot(1, 7, 30, builder.Capture(world));

        Assert(firstPlan.Snapshot.Players.Select(player => player.PlayerId).SequenceEqual(new[] { 1, 2 }), "first observation must initialize both self and low-priority player");
        Assert(secondPlan.Snapshot.Players.Select(player => player.PlayerId).SequenceEqual(new[] { 1 }), "low-priority remote state should not be resent on every snapshot");
        Assert(secondPlan.Snapshot.ReplicatedPlayerIds.SequenceEqual(new[] { 1, 2 }), "low-priority remote must remain in scope while its state is paced");
        Assert(sixthTickPlan.Snapshot.Players.Select(player => player.PlayerId).SequenceEqual(new[] { 1, 2 }), "low-priority remote should be resent after its configured six-tick interval");
    }

    private static void FullSnapshotModePreservesTheChapterOneBaseline()
    {
        GameWorld world = CreateWorldWithThreePlayers();
        SnapshotBuilder builder = new SnapshotBuilder();
        ClientReplicator replicator = new ClientReplicator(new ClientReplicationSettings
        {
            EnableDistanceFiltering = false,
            CombatPriorityDurationTicks = 0
        });

        ClientSnapshotPlan plan = replicator.BuildSnapshot(1, 1, 30, builder.Capture(world));

        Assert(plan.Snapshot.Players.Select(player => player.PlayerId).SequenceEqual(new[] { 1, 2, 3 }), "full mode must send every player just like the chapter-one broadcast");
        Assert(plan.Snapshot.ReplicatedPlayerIds.SequenceEqual(new[] { 1, 2, 3 }), "full mode must keep every player in scope");
    }

    private static void DistanceFilteringIsTheDefaultReplicationMode()
    {
        ClientReplicationSettings settings = new ClientReplicationSettings();
        Assert(settings.EnableDistanceFiltering, "phase-11 settings should enable distance filtering unless full-snapshot mode is explicitly selected");
    }

    private static void InactiveClientsAreRemovedFromTheRegistry()
    {
        ClientRegistry registry = new ClientRegistry();
        ClientReplicationSettings settings = new ClientReplicationSettings();
        IPEndPoint endpoint = new IPEndPoint(IPAddress.Loopback, 9001);
        ClientRegistration registration = registry.RegisterOrUpdate("TimeoutTest", endpoint, settings);

        List<ConnectedClient> removed = registry.RemoveInactive(DateTime.UtcNow.AddSeconds(9), TimeSpan.FromSeconds(8));

        Assert(removed.Count == 1 && removed[0].PlayerId == registration.Client.PlayerId, "silent client should be removed after the timeout window");
        Assert(registry.Count == 0, "expired client must no longer remain in the registry");
    }

    private static GameWorld CreateWorldWithThreePlayers()
    {
        GameWorld world = CreateWorld();
        Assert(world.AddPlayer(1), "player 1 should be added");
        Assert(world.AddPlayer(2), "player 2 should be added");
        Assert(world.AddPlayer(3), "player 3 should be added");
        return world;
    }

    private static GameWorld CreateWorld()
    {
        return new GameWorld(new GameWorldSettings
        {
            MaxInputTickAhead = 30,
            MaxInputTickLag = 120,
            InputBufferCapacity = 64
        });
    }

    private static InputCommand Input(int inputTick, float moveAxis)
    {
        return new InputCommand(inputTick, moveAxis, 0f, 0f, 5f, false);
    }

    private static FireCommand Fire(int requestTick, float aimX, float aimZ, float directionX, float directionY, float directionZ)
    {
        return new FireCommand(
            requestTick,
            aimX,
            aimZ,
            0f,
            1.2f,
            0f,
            directionX,
            directionY,
            directionZ,
            0.1f,
            0.1f);
    }

    private static void AssertAccepted(CommandGateResult result, string description)
    {
        Assert(result.IsAccepted, $"Expected accepted {description}, got '{result.Reason}'.");
    }

    private static void AssertRejected(CommandGateResult result, string description)
    {
        Assert(!result.IsAccepted, $"Expected rejected {description}.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
