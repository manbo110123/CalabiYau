internal static class Program
{
    private static void Main()
    {
        DuplicateAndOutOfOrderInputsDoNotMoveTheWorldBackward();
        InvalidAndFutureInputsAreRejectedAtTheWorldGate();
        DeadPlayersCannotQueueInputsOrFireRequests();

        Console.WriteLine("GameWorld command timeline checks passed.");
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
