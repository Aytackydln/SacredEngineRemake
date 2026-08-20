namespace Sacred.Core.Analyzer;

internal sealed record GameFileDefinition(
    string PathPattern,
    string Description,
    IReadOnlyList<GameFileSection> Sections);

internal sealed record GameFileSection(
    string Name,
    string LayoutTypeName,
    string Repetition,
    string? Notes = null);

internal sealed record DiscoveredGameFile(string RelativePath, long Length);

internal sealed record FieldCoverage(
    string Name,
    string TypeName,
    int Offset,
    int Size,
    bool IsKnown,
    bool IsString,
    string Documentation);

internal sealed record ByteRange(int Offset, int Length)
{
    public int End => Offset + Length - 1;
}

internal sealed record LayoutCoverage(
    string TypeName,
    string Namespace,
    int Size,
    string Documentation,
    IReadOnlyList<FieldCoverage> Fields,
    IReadOnlyList<ByteRange> UnknownRanges)
{
    public int KnownFieldCount => Fields.Count(static item => item.IsKnown);
    public int KnownByteCount => Size - UnknownRanges.Sum(static range => range.Length);
}
