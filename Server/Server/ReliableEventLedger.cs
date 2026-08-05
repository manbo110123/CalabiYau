// A deliberately small reliability layer for a few important game results. It is
// independent from snapshots and only tracks one recipient's unacknowledged events.
public sealed class ReliableEventSettings
{
    public float ResendIntervalSeconds { get; set; } = 0.25f;
    public int MaximumResendCount { get; set; } = 4;
}

public sealed class ReliableEventLedger
{
    private readonly Dictionary<long, PendingReliableEvent> pendingByEventId = new Dictionary<long, PendingReliableEvent>();
    private readonly TimeSpan resendInterval;
    private readonly int maximumResendCount;

    public ReliableEventLedger(TimeSpan resendInterval, int maximumResendCount)
    {
        this.resendInterval = resendInterval < TimeSpan.Zero ? TimeSpan.Zero : resendInterval;
        this.maximumResendCount = Math.Max(0, maximumResendCount);
    }

    public int PendingCount => pendingByEventId.Count;
    public long ResendCount { get; private set; }
    public long AcknowledgedCount { get; private set; }
    public long RetryLimitExceededCount { get; private set; }
    public double TotalAcknowledgementLatencyMilliseconds { get; private set; }

    public void QueueInitial(long eventId, string json, int serverTick, int[] relatedPlayerIds, DateTime nowUtc)
    {
        if (eventId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(eventId));
        }

        pendingByEventId[eventId] = new PendingReliableEvent(eventId, json, serverTick, relatedPlayerIds, nowUtc);
    }

    public bool Acknowledge(long eventId, DateTime nowUtc)
    {
        if (!pendingByEventId.Remove(eventId, out PendingReliableEvent? pending))
        {
            return false;
        }

        AcknowledgedCount++;
        TotalAcknowledgementLatencyMilliseconds += Math.Max(0d, (nowUtc - pending.FirstSentUtc).TotalMilliseconds);
        return true;
    }

    public void DiscardOutsideReplicationScope(Func<PendingReliableEvent, bool> shouldRemainPending)
    {
        foreach (PendingReliableEvent pending in pendingByEventId.Values.Where(pending => !shouldRemainPending(pending)).ToArray())
        {
            pendingByEventId.Remove(pending.EventId);
        }
    }

    public List<PendingReliableEvent> CollectDueResends(DateTime nowUtc)
    {
        List<PendingReliableEvent> due = new List<PendingReliableEvent>();

        foreach (PendingReliableEvent pending in pendingByEventId.Values.ToArray())
        {
            if (nowUtc - pending.LastSentUtc < resendInterval)
            {
                continue;
            }

            if (pending.ResendCount >= maximumResendCount)
            {
                pendingByEventId.Remove(pending.EventId);
                RetryLimitExceededCount++;
                continue;
            }

            pending.MarkResent(nowUtc);
            ResendCount++;
            due.Add(pending);
        }

        return due;
    }

    public ReliableEventLedgerTelemetry GetTelemetry()
    {
        double averageAckLatency = AcknowledgedCount == 0
            ? 0d
            : TotalAcknowledgementLatencyMilliseconds / AcknowledgedCount;
        return new ReliableEventLedgerTelemetry(PendingCount, ResendCount, AcknowledgedCount, averageAckLatency, RetryLimitExceededCount);
    }
}

public sealed class PendingReliableEvent
{
    public PendingReliableEvent(long eventId, string json, int serverTick, int[] relatedPlayerIds, DateTime firstSentUtc)
    {
        EventId = eventId;
        Json = json;
        ServerTick = serverTick;
        RelatedPlayerIds = relatedPlayerIds;
        FirstSentUtc = firstSentUtc;
        LastSentUtc = firstSentUtc;
        SendCount = 1;
    }

    public long EventId { get; }
    public string Json { get; }
    public int ServerTick { get; }
    public int[] RelatedPlayerIds { get; }
    public DateTime FirstSentUtc { get; }
    public DateTime LastSentUtc { get; private set; }
    public int SendCount { get; private set; }
    public int ResendCount => SendCount - 1;

    public void MarkResent(DateTime nowUtc)
    {
        LastSentUtc = nowUtc;
        SendCount++;
    }
}

public readonly struct ReliableEventLedgerTelemetry
{
    public ReliableEventLedgerTelemetry(int pendingCount, long resendCount, long acknowledgedCount, double averageAcknowledgementLatencyMilliseconds, long retryLimitExceededCount)
    {
        PendingCount = pendingCount;
        ResendCount = resendCount;
        AcknowledgedCount = acknowledgedCount;
        AverageAcknowledgementLatencyMilliseconds = averageAcknowledgementLatencyMilliseconds;
        RetryLimitExceededCount = retryLimitExceededCount;
    }

    public int PendingCount { get; }
    public long ResendCount { get; }
    public long AcknowledgedCount { get; }
    public double AverageAcknowledgementLatencyMilliseconds { get; }
    public long RetryLimitExceededCount { get; }
}

// This is intentionally a bounded recent-ID set, not an unbounded event history.
// The Unity client has the same implementation because it compiles in a different project.
public sealed class RecentReliableEventIds
{
    private readonly int capacity;
    private readonly HashSet<long> ids = new HashSet<long>();
    private readonly Queue<long> order = new Queue<long>();

    public RecentReliableEventIds(int capacity)
    {
        this.capacity = Math.Max(1, capacity);
    }

    public int Count => ids.Count;

    public bool TryRecord(long eventId)
    {
        if (!ids.Add(eventId))
        {
            return false;
        }

        order.Enqueue(eventId);
        while (order.Count > capacity)
        {
            ids.Remove(order.Dequeue());
        }

        return true;
    }
}
