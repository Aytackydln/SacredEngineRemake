namespace Sacred.Core.Runtime;

/// <summary>Original 0x663A10 unsigned bytewise ordering followed by length.
/// Case and embedded NUL bytes remain significant in stored keys.</summary>
public sealed class SacredSettingsKeyComparer : IComparer<ReadOnlyMemory<byte>>
{
    public static SacredSettingsKeyComparer Instance { get; } = new();
    private SacredSettingsKeyComparer() { }
    public int Compare(ReadOnlyMemory<byte> left, ReadOnlyMemory<byte> right) => left.Span.SequenceCompareTo(right.Span);
}
