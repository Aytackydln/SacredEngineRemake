using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Sacred.Core.Binary;

namespace Sacred.Core.GameBin.Scripts;

/// <summary>Gold interpreter +0x1048, saved by 0x467069 and transferred in bank 1's
/// second fixed block. CreateNPC 0x482F55 indexes these records by the raw region.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public readonly struct SacredScriptRegionReplacementLayout
{
    public const int SerializedSize = 172;
    public const int InterpreterOffset = 0x1048;
    /// <summary>Gold dispatcher 0x476009 calls the mutation parser at 0x4ABB60.
    /// The same table helper is called by network opcode 0x189.</summary>
    public const ushort SetOpcode = 116;
    public const int RegionCount = 50;
    public const int TableSize = SerializedSize * RegionCount;
    public const int ReplacementCountOffset = 8;
    public const int EntriesOffset = 12;
    public const int EntryCapacity = 20;
    /// <summary>Gold 0x4AC062..0x4AC076 shifts this mask left 16 and ORs it
    /// into every replacement entry's packed target/behavior word.</summary>
    [FieldOffset(0)] public readonly uint Word00;
    public uint ReplacementBehaviorMask => Word00;
    [FieldOffset(4), BinaryUnknown] public readonly uint Word04;
    /// <summary>Signed native count; nonpositive counts skip substitution.</summary>
    [FieldOffset(ReplacementCountOffset)] public readonly int ReplacementCount;
    [FieldOffset(EntriesOffset)] public readonly SacredScriptRegionReplacementEntries Entries;
}

[InlineArray(SacredScriptRegionReplacementLayout.EntryCapacity)]
public struct SacredScriptRegionReplacementEntries { private SacredScriptRegionReplacementEntryLayout _first; }

[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public readonly struct SacredScriptRegionReplacementEntryLayout
{
    public const int SerializedSize = 8;
    [FieldOffset(0)] public readonly uint SourceTypeId;
    [FieldOffset(4)] public readonly ushort ReplacementTypeId;
    /// <summary>Zero preserves behavior. Otherwise native SAR sign-extends this word and
    /// ORs it with the original behavior's high word, including sign-bit effects.</summary>
    [FieldOffset(6)] public readonly ushort BehaviorSelector;
}

public readonly record struct SacredScriptCreatureReplacement(uint TypeId, uint BehaviorFlags, bool Matched);
