using Sacred.Network.Protocol;

namespace Sacred.Network.Gameplay;

/// <summary>World-trigger grant cursor at native cWorld +0x274/+0x278.
/// Replenishment replaces the cursor, as in the client receive path.</summary>
public sealed class GoldTriggerReferencePool
{
    public uint Next { get; private set; }
    public uint EndExclusive { get; private set; }
    public bool HasGrant { get; private set; }
    public uint Available => HasGrant && Next != 0 && Next < EndExclusive ? EndExclusive - Next : 0;
    public bool NeedsReplenishment => Available < 6;
    public bool TryAllocate(out uint reference)
    {
        if (Available == 0 || Next == 0) { reference = 0; return false; }
        reference = Next++;
        return true;
    }
    /// <param name="authoredTableBound">Native world trigger bound loaded from the original world,
    /// required for the first request. The character object count is unrelated.</param>
    public GoldMessage CreateRequest(byte playerSlot, uint authoredTableBound) => GoldReferenceCodec.CreateTriggerRangeRequest(
        HasGrant ? Math.Max(EndExclusive, authoredTableBound) : authoredTableBound, GoldReferenceCodec.TriggerRangeRequestCount, playerSlot);
    internal void Apply(GoldReferenceRangeEvent grant)
    {
        // Keep native wrapping arithmetic observable. An unusable wrapped range cannot allocate IDs.
        Next = grant.Start;
        EndExclusive = grant.EndExclusive;
        HasGrant = true;
    }
    internal void Clear() { Next = EndExclusive = 0; HasGrant = false; }
}
