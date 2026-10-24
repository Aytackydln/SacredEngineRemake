namespace Sacred.Network.Gameplay;

/// <summary>Local journal end slot from Gold 0x49DA50/0x49DAB0. No initial value is inferred from an update.</summary>
public sealed class GoldQuestJournalRange
{
    private readonly Queue<(uint Index, uint Count)> _pending = [];
    public uint? End { get; private set; }
    public int PendingCount => _pending.Count;
    public void Configure(uint end)
    {
        if (End.HasValue) return;
        End = end;
        while (_pending.TryDequeue(out var work)) Apply(work.Index, work.Count);
    }
    internal void Apply(uint index, uint count)
    {
        if (!End.HasValue) _pending.Enqueue((index, count));
        else if (End == index) End = count;
    }
    internal void Clear() { End = null; _pending.Clear(); }
}
