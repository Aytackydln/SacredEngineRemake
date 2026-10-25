using Sacred.Network.Protocol;

namespace Sacred.Network.Gameplay;

/// <summary>Ordered receipt-time work, independent of each replica's diagnostic history.</summary>
public sealed record GoldConsumerWork(long Session, long Order, string Family, ushort Opcode,
    byte Sender, byte Origin, uint Sequence, object Value);

public enum GoldConsumerOutcome { Consumed, Deferred, Rejected }

public sealed record GoldConsumerReport(string Consumer, GoldConsumerWork Work, GoldConsumerOutcome Outcome, string Detail);

/// <summary>Main-thread bounded delivery journal. Independent readers explicitly detect retention loss.
/// Overflow is a failed delivery boundary, never successful consumption or implicit resynchronization.</summary>
public sealed class GoldConsumerDelivery
{
    private readonly GoldConsumerWork?[] _entries;
    private readonly Queue<GoldConsumerReport> _reports = [];
    public GoldConsumerDelivery(int capacity = 4096)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        _entries = new GoldConsumerWork[capacity];
    }
    public long Session { get; private set; }
    public long Published { get; private set; }
    public long Evicted { get; private set; }
    public int Capacity => _entries.Length;
    public GoldConsumerDiagnostics Diagnostics { get; } = new();
    public IReadOnlyCollection<GoldConsumerReport> Reports => _reports;
    public event Action<GoldConsumerReport>? Reported;
    internal void Publish(string family, GoldMessage source, object value)
    {
        var order = ++Published;
        if (order > Capacity) Evicted++;
        _entries[(int)((order - 1) % Capacity)] = new(Session, order, family,
            source.Opcode, source.Sender, source.Origin, source.Sequence, value);
        Diagnostics.Publish(_entries[(int)((order - 1) % Capacity)]!);
    }
    public GoldConsumerCursor CreateCursor(string name, Func<GoldConsumerWork, bool> accepts) => new(this, name, accepts);
    /// <summary>Explicitly discard a consumer's old playback when its owner clears/rebuilds the scene.</summary>
    public void ResetToPresent(GoldConsumerCursor cursor)
    { Validate(cursor); cursor.Reset(Session); cursor.Next = Published + 1; Diagnostics.ClearFault(cursor.Name); }
    public bool TryPeek(GoldConsumerCursor cursor, out GoldConsumerWork work)
    {
        Validate(cursor);
        if (cursor.Session != Session) cursor.Reset(Session);
        var first = Math.Max(1, Published - Capacity + 1);
        if (cursor.Next < first)
        {
            cursor.Lost = first - cursor.Next;
            Diagnostics.Fault(cursor.Name, cursor.Lost);
            work = null!; return false;
        }
        while (cursor.Next <= Published)
        {
            var entry = _entries[(int)((cursor.Next - 1) % Capacity)]!;
            if (cursor.Accepts(entry)) { work = entry; return true; }
            cursor.Next++;
        }
        work = null!; return false;
    }
    public void Report(GoldConsumerCursor cursor, GoldConsumerWork work, GoldConsumerOutcome outcome, string detail)
    {
        Validate(cursor);
        if (work.Session != Session || cursor.Session != Session || work.Order != cursor.Next ||
            !ReferenceEquals(_entries[(int)((work.Order - 1) % Capacity)], work))
            throw new InvalidOperationException("Consumer completion does not match its current session and work.");
        // A deferred head stays available; only report a changed dependency, not every frame.
        var report = new GoldConsumerReport(cursor.Name, work, outcome, detail);
        if (cursor.LastReport != report)
        {
            _reports.Enqueue(report); if (_reports.Count > 128) _reports.Dequeue();
            cursor.LastReport = report; Diagnostics.Report(report); Reported?.Invoke(report);
        }
        if (outcome != GoldConsumerOutcome.Deferred) cursor.Next++;
    }
    internal void Clear()
    {
        Array.Clear(_entries); _reports.Clear(); Published = Evicted = 0; Session++;
        Diagnostics.Clear();
    }
    private void Validate(GoldConsumerCursor cursor)
    {
        if (!ReferenceEquals(cursor.Owner, this)) throw new InvalidOperationException("Consumer cursor belongs to a different world delivery journal.");
    }
}

public sealed class GoldConsumerCursor
{
    internal GoldConsumerCursor(GoldConsumerDelivery owner, string name, Func<GoldConsumerWork, bool> accepts)
    { Owner = owner; Name = name; Accepts = accepts; }
    internal GoldConsumerDelivery Owner { get; }
    public string Name { get; }
    public long Session { get; private set; } = -1;
    public long Next { get; internal set; } = 1;
    public long Lost { get; internal set; }
    internal Func<GoldConsumerWork, bool> Accepts { get; }
    internal GoldConsumerReport? LastReport { get; set; }
    internal void Reset(long session) { Session = session; Next = 1; Lost = 0; LastReport = null; }
}
