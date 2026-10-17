namespace Sacred.Network.Gameplay;

/// <summary>Gold bank 1's uncounted region table. Client 0x61D450 initializes it;
/// server 0x477810 agrees. Unresolved fields retain their offsets.</summary>
public static class GoldWorldRegionTableLayout
{
    public const int Count = 256;
    public const int RecordSize = 24;
    public const int Size = Count * RecordSize;
    public const int IndexOffset = 0;
    public const int Byte2Offset = 2;
    public const int Word4Offset = 4;
    public const int FlagsOffset = 8;
    public const int Float12Offset = 12;
    public const int Float16Offset = 16;
    public const int Float20Offset = 20;
    /// <summary>0x492880 sets this before invoking the region initialization function.
    /// It is distinct from the interpreter's per-sector initialization map.</summary>
    public const uint InitializedFlag = 1;
}
