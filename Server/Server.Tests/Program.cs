using System.Net;

internal static class Program
{
    private static void Main()
    {
        DuplicateAndOutOfOrderInputsDoNotMoveTheWorldBackward();
        InputLeaseStopsMovementAfterCommandsExpire();
        InvalidAndFutureInputsAreRejectedAtTheWorldGate();
        FireCommandsResolveOnlyInsideTheServerTick();
        DuplicateFireSequenceQueuesOnceAndReplaysTheSameReceipt();
        InvalidFireRequestReturnsAndReplaysARejectedReceipt();
        DeadPlayersCannotQueueInputsOrFireRequests();
        PerClientReplicationAlwaysSendsSelfAndFiltersOutOfRangePlayers();
        LowPriorityEntitiesKeepTheirScopeButUseALowerStateRate();
        FullSnapshotModePreservesTheChapterOneBaseline();
        SnapshotSequenceIsIndependentFromServerTick();
        DistanceFilteringIsTheDefaultReplicationMode();
        InactiveClientsAreRemovedFromTheRegistry();

        Console.WriteLine("GameWorld command timeline, reliable fire receipt, and stage-11 replication checks passed.");
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
        AssertRejected(world.TryQueueInput(1, new InputCommand(1, 0f, 0f, float.NaN, 5f)), "NaN aim value");
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

        AssertAccepted(world.TryQueueFire(1, Fire(1, 1, 4f, 0f)), "killing fire request");
        world.Tick(1f / 30f);
        PlayerState playerTwo = world.Players.Single(player => player.PlayerId == 2);
        Assert(!playerTwo.IsAlive, "player 2 should be dead after the authoritative hit");

        AssertRejected(world.TryQueueInput(2, Input(1, 1f)), "input from a dead player");
        AssertRejected(world.TryQueueFire(2, Fire(1, 1, 0f, 0f)), "fire request from a dead player");
        Assert(world.FireRejectionsByReason.ContainsKey("player-dead"), "dead fire rejection should be observable");
    }

    private static void InputLeaseStopsMovementAfterCommandsExpire()
    {
        GameWorld world = new GameWorld(new GameWorldSettings { InputHoldTimeoutTicks = 2 });
        Assert(world.AddPlayer(1), "player 1 should be added");
        AssertAccepted(world.TryQueueInput(1, Input(1, 1f)), "initial movement input");

        world.Tick(1f / 30f);
        world.Tick(1f / 30f);
        world.Tick(1f / 30f);
        float positionBeforeExpiry = world.Players.Single().Z;

        world.Tick(1f / 30f);
        Assert(world.Players.Single().Z == positionBeforeExpiry, "movement must stop when the input lease expires");
    }

    private static void FireCommandsResolveOnlyInsideTheServerTick()
    {
        GameWorld world = new GameWorld(new GameWorldSettings { FireCooldownSeconds = 0f, AimToleranceMeters = 50f });
        Assert(world.AddPlayer(1), "player 1 should be added");
        Assert(world.AddPlayer(2), "player 2 should be added");

        AssertAccepted(world.TryQueueFire(1, Fire(1, 1, 4f, 0f)), "first fire command");
        AssertRejected(world.TryQueueFire(1, Fire(1, 1, 4f, 0f)), "duplicate fire sequence");
        Assert(world.Players.Single(player => player.PlayerId == 2).Health == world.MaxHealth, "queued fire must not damage before Tick");

        List<GameWorldEvent> events = world.Tick(1f / 30f);
        Assert(events.OfType<FireResolvedEvent>().Any(), "Tick should resolve the queued fire command");
        Assert(world.Players.Single(player => player.PlayerId == 2).Health < world.MaxHealth, "resolved fire should apply authoritative damage");
    }

    private static void DuplicateFireSequenceQueuesOnceAndReplaysTheSameReceipt()
    {
        GameWorld world = new GameWorld(new GameWorldSettings { FireCooldownSeconds = 0f, AimToleranceMeters = 50f });
        Assert(world.AddPlayer(1), "player 1 should be added");
        Assert(world.AddPlayer(2), "player 2 should be added");

        FireReceiptDecision firstReceipt = world.TryQueueFireWithReceipt(1, Fire(1, 1, 4f, 0f));
        FireReceiptDecision retryReceipt = world.TryQueueFireWithReceipt(1, Fire(1, 1, 4f, 0f));

        Assert(firstReceipt.Accepted, "the first valid FireRequest should receive an accepted receipt");
        Assert(!firstReceipt.IsDuplicate, "the first receipt must not be marked as a retry");
        Assert(retryReceipt.IsDuplicate, "the repeated fireSequence should replay a cached receipt");
        AssertSameReceipt(firstReceipt, retryReceipt, "accepted retry receipt");
        Assert(world.QueuedFireRequestCount == 1, "a repeated fireSequence must queue exactly one fire command");

        List<GameWorldEvent> events = world.Tick(1f / 30f);
        Assert(events.OfType<FireResolvedEvent>().Count() == 1, "one queued fire command should resolve once");
    }

    private static void InvalidFireRequestReturnsAndReplaysARejectedReceipt()
    {
        GameWorld world = CreateWorld();
        Assert(world.AddPlayer(1), "player 1 should be added");

        FireReceiptDecision firstReceipt = world.TryQueueFireWithReceipt(1, Fire(7, 0, 4f, 0f));
        FireReceiptDecision retryReceipt = world.TryQueueFireWithReceipt(1, Fire(7, 0, 4f, 0f));

        Assert(!firstReceipt.Accepted, "an invalid FireRequest should receive a rejected receipt");
        Assert(firstReceipt.Reason == "invalid-fire-command", "the rejected receipt should contain the gate reason");
        Assert(retryReceipt.IsDuplicate, "an invalid retry should replay the first rejected receipt");
        AssertSameReceipt(firstReceipt, retryReceipt, "rejected retry receipt");
        Assert(world.RejectedFireRequestCount == 1, "a repeated invalid fireSequence must not be rejected twice");
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

    private static void SnapshotSequenceIsIndependentFromServerTick()
    {
        GameWorld world = CreateWorldWithThreePlayers();
        SnapshotBuilder builder = new SnapshotBuilder();
        ClientReplicator replicator = new ClientReplicator(new ClientReplicationSettings());

        ClientSnapshotPlan firstPlan = replicator.BuildSnapshot(1, 30, 30, builder.Capture(world));
        ClientSnapshotPlan secondPlan = replicator.BuildSnapshot(1, 30, 30, builder.Capture(world));

        Assert(firstPlan.Snapshot.SnapshotSequence == 1, "first snapshot sequence should start at one");
        Assert(secondPlan.Snapshot.SnapshotSequence == 2, "every datagram needs its own sequence even at the same simulation Tick");
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
        return new InputCommand(inputTick, moveAxis, 0f, 0f, 5f);
    }

    private static FireCommand Fire(int fireSequence, int requestTick, float aimX, float aimZ)
    {
        return new FireCommand(
            fireSequence,
            requestTick,
            aimX,
            aimZ,
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

    private static void AssertSameReceipt(FireReceiptDecision expected, FireReceiptDecision actual, string description)
    {
        Assert(expected.FireSequence == actual.FireSequence, $"{description} sequence must remain stable");
        Assert(expected.Accepted == actual.Accepted, $"{description} acceptance must remain stable");
        Assert(expected.Reason == actual.Reason, $"{description} reason must remain stable");
        Assert(expected.ServerTick == actual.ServerTick, $"{description} server tick must remain stable");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
