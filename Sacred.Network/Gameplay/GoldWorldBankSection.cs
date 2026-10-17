namespace Sacred.Network.Gameplay;

public sealed class GoldWorldBankSection
{
    internal GoldWorldBankSection(int index, GoldWorldBankSectionKind kind, int elementSize, GoldWorldBankRecord[] records)
    {
        Index = index;
        Kind = kind;
        ElementSize = elementSize;
        Records = Array.AsReadOnly(records);
    }

    public int Index { get; }
    public GoldWorldBankSectionKind Kind { get; }
    public int ElementSize { get; }
    public IReadOnlyList<GoldWorldBankRecord> Records { get; }
    public int Count => Records.Count;
}
