using Sacred.Core.GameBin.Scripts;

namespace Sacred.Network.Gameplay;

/// <summary>Bank 1's uncounted blocks before section 0. The final block is the native region table.</summary>
public sealed class GoldWorldBankGlobals
{
    public const int FirstBlockSize = SacredScriptStartupParameterLayout.BankBlockSize;
    public const int SecondBlockSize = SacredScriptRegionReplacementLayout.TableSize;
    public const int TableCount = GoldWorldRegionTableLayout.Count;
    public const int TableRecordSize = GoldWorldRegionTableLayout.RecordSize;
    public const int Size = FirstBlockSize + SecondBlockSize + TableCount * TableRecordSize;
    private readonly ReadOnlyMemory<byte> _data;
    internal GoldWorldBankGlobals(ReadOnlyMemory<byte> data) => _data = data;
    /// <summary>Interpreter +0x7684: first 32 opcode-107 parameter words, including untouched storage.</summary>
    public ReadOnlySpan<byte> FirstBlock => _data.Span[..FirstBlockSize];
    /// <summary>Interpreter +0x1048: 50 regional NPC replacement records of 172 bytes.</summary>
    public ReadOnlySpan<byte> SecondBlock => _data.Span.Slice(FirstBlockSize, SecondBlockSize);
    public ReadOnlySpan<byte> Table => _data.Span[(FirstBlockSize + SecondBlockSize)..];
    public GoldWorldRegionTable ReadRegions() => GoldWorldRegionTable.FromPayload(Table);
    public ReadOnlySpan<byte> GetTableRecord(int index)
    {
        if ((uint)index >= TableCount) throw new ArgumentOutOfRangeException(nameof(index));
        return Table.Slice(index * TableRecordSize, TableRecordSize);
    }
}
