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
        PerClientSnapshotsPreserveLifecycleVersion();
        DistanceFilteringIsTheDefaultReplicationMode();
        InactiveClientsAreRemovedFromTheRegistry();
        ReliableEventsResendOnlyAfterTheirInterval();
        AcknowledgedReliableEventsDoNotResend();
        ReliableEventsStopAfterTheirRetryLimit();
        DuplicateReliableEventIdsAreAppliedOnlyOnce();
        ReliableEventDeduplicationIsLimitedToItsConfiguredWindow();
        LifecycleEventsCarryPerPlayerMonotonicVersions();
        SnapshotsContinueWhileReliableEventsAwaitAcknowledgement();

        Console.WriteLine("GameWorld command timeline, reliable fire receipt, replication, and reliable result-event checks passed.");
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

    private static void PerClientSnapshotsPreserveLifecycleVersion()
    {
        GameWorld world = CreateWorldWithThreePlayers();
        PlayerState playerTwo = world.Players.Single(player => player.PlayerId == 2);
        playerTwo.LifeStateVersion = 7;

        ClientSnapshotPlan plan = new ClientReplicator(new ClientReplicationSettings
        {
            EnableDistanceFiltering = false
        }).BuildSnapshot(1, 1, 30, new SnapshotBuilder().Capture(world));

        PlayerSnapshotMessage replicatedPlayerTwo = plan.Snapshot.Players.Single(player => player.PlayerId == 2);
        Assert(replicatedPlayerTwo.LifeStateVersion == 7,
            "per-client full snapshots must preserve lifecycle version for client-side stale-event rejection");
    }

    private static void ReliableEventsResendOnlyAfterTheirInterval()
    {
        ReliableEventLedger ledger = new ReliableEventLedger(TimeSpan.FromMilliseconds(100), 2);
        DateTime firstSend = DateTime.UtcNow;
        ledger.QueueInitial(1, "{\"type\":\"DeathEvent\",\"eventId\":1}", 10, new[] { 2 }, firstSend);

        Assert(ledger.CollectDueResends(firstSend.AddMilliseconds(99)).Count == 0, "a reliable event must not resend before its interval");
        List<PendingReliableEvent> firstResend = ledger.CollectDueResends(firstSend.AddMilliseconds(100));
        Assert(firstResend.Count == 1 && firstResend[0].EventId == 1, "an unacknowledged reliable event must resend at its interval");
        Assert(firstResend[0].ResendCount == 1, "the ledger should retain the per-event resend count");
        Assert(ledger.GetTelemetry().ResendCount == 1, "resend telemetry should record the resend");
    }

    private static void AcknowledgedReliableEventsDoNotResend()
    {
        ReliableEventLedger ledger = new ReliableEventLedger(TimeSpan.FromMilliseconds(100), 2);
        DateTime firstSend = DateTime.UtcNow;
        ledger.QueueInitial(3, "{\"type\":\"RespawnEvent\",\"eventId\":3}", 20, new[] { 1 }, firstSend);

        Assert(ledger.Acknowledge(3, firstSend.AddMilliseconds(25)), "the matching client acknowledgement should remove its pending event");
        Assert(ledger.PendingCount == 0, "acknowledged event must leave that client ledger");
        Assert(ledger.CollectDueResends(firstSend.AddSeconds(1)).Count == 0, "an acknowledged event must never resend");
        Assert(ledger.GetTelemetry().AcknowledgedCount == 1, "acknowledgement telemetry should be observable");
    }

    private static void ReliableEventsStopAfterTheirRetryLimit()
    {
        ReliableEventLedger ledger = new ReliableEventLedger(TimeSpan.FromMilliseconds(100), 1);
        DateTime firstSend = DateTime.UtcNow;
        ledger.QueueInitial(5, "{\"type\":\"KillEvent\",\"eventId\":5}", 30, new[] { 1, 2 }, firstSend);

        Assert(ledger.CollectDueResends(firstSend.AddMilliseconds(100)).Count == 1, "the configured first resend should be sent");
        Assert(ledger.CollectDueResends(firstSend.AddMilliseconds(200)).Count == 0, "the event should stop once its resend limit is reached");
        Assert(ledger.PendingCount == 0, "retry-limit events must not remain pending forever");
        Assert(ledger.GetTelemetry().RetryLimitExceededCount == 1, "retry-limit telemetry should record the capped event");
    }

    private static void DuplicateReliableEventIdsAreAppliedOnlyOnce()
    {
        RecentReliableEventIds received = new RecentReliableEventIds(2);
        int appliedCount = 0;

        if (received.TryRecord(42))
        {
            appliedCount++;
        }

        if (received.TryRecord(42))
        {
            appliedCount++;
        }

        Assert(appliedCount == 1, "the same reliable event id should be applied only once");
        Assert(received.Count == 1, "duplicate reliable ids must not grow the recent-id set");
    }

    private static void ReliableEventDeduplicationIsLimitedToItsConfiguredWindow()
    {
        RecentReliableEventIds received = new RecentReliableEventIds(2);

        Assert(received.TryRecord(1), "the first event should be new");
        Assert(received.TryRecord(2), "the second event should be new");
        Assert(received.TryRecord(3), "the third event should evict the oldest entry");
        Assert(received.Count == 2, "the recent-id set must stay at its configured capacity");
        Assert(received.TryRecord(1), "an id outside the finite window is no longer deduplicated");
    }

    private static void LifecycleEventsCarryPerPlayerMonotonicVersions()
    {
        GameWorld world = new GameWorld(new GameWorldSettings
        {
            FireDamage = 100,
            FireCooldownSeconds = 0f,
            RespawnDelaySeconds = 1f / 30f,
            AimToleranceMeters = 50f
        });
        Assert(world.AddPlayer(1), "player 1 should be added");
        Assert(world.AddPlayer(2), "player 2 should be added");

        AssertAccepted(world.TryQueueFire(1, Fire(1, 1, 4f, 0f)), "killing fire request");
        List<GameWorldEvent> deathTickEvents = world.Tick(1f / 30f);
        DeathWorldEvent death = deathTickEvents.OfType<DeathWorldEvent>().Single();
        PlayerState victim = world.Players.Single(player => player.PlayerId == 2);

        Assert(death.LifeStateVersion == 2, "death must advance the victim life-state version");
        Assert(victim.LifeStateVersion == death.LifeStateVersion, "death event and authority state must agree");

        List<GameWorldEvent> respawnTickEvents = world.Tick(1f / 30f);
        RespawnWorldEvent respawn = respawnTickEvents.OfType<RespawnWorldEvent>().Single();

        Assert(respawn.LifeStateVersion == 3, "respawn must advance the same player's version again");
        Assert(respawn.LifeStateVersion > death.LifeStateVersion, "later lifecycle results must be strictly newer");

        PlayerSnapshotMessage snapshot = new SnapshotBuilder().Build(world).Players.Single(player => player.PlayerId == 2);
        Assert(snapshot.LifeStateVersion == respawn.LifeStateVersion, "snapshots must carry the latest lifecycle version as recovery state");
    }

    private static void SnapshotsContinueWhileReliableEventsAwaitAcknowledgement()
    {
        GameWorld world = CreateWorldWithThreePlayers();
        SnapshotBuilder builder = new SnapshotBuilder();
        ClientReplicator replicator = new ClientReplicator(new ClientReplicationSettings());
        ReliableEventLedger ledger = new ReliableEventLedger(TimeSpan.FromSeconds(1), 1);
        ledger.QueueInitial(9, "{\"type\":\"DeathEvent\",\"eventId\":9}", 1, new[] { 2 }, DateTime.UtcNow);

        ClientSnapshotPlan firstSnapshot = replicator.BuildSnapshot(1, 1, 30, builder.Capture(world));
        ClientSnapshotPlan nextSnapshot = replicator.BuildSnapshot(1, 2, 30, builder.Capture(world));

        Assert(ledger.PendingCount == 1, "the test event should still be awaiting acknowledgement");
        Assert(firstSnapshot.Snapshot.SnapshotSequence == 1 && nextSnapshot.Snapshot.SnapshotSequence == 2,
            "WorldSnapshot emission must continue independently while an event awaits acknowledgement");
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
