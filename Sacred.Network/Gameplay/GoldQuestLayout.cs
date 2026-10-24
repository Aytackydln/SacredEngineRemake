namespace Sacred.Network.Gameplay;

/// <summary>Gold client quest deltas at 0x806620..0x80736D; offsets are wire offsets unless stated.</summary>
public static class GoldQuestLayout
{
    public const ushort ProgressOpcode = 0x1B8;
    public const ushort InsertOpcode = 0x1B9;
    public const ushort EntryOpcode = 0x1BA;
    public const ushort HeaderOpcode = 0x1BB;
    public const ushort DatabaseOpcode = 0x1BC;
    public const ushort JournalOpcode = 0x1BD;
    public const ushort DatabaseAndEntriesOpcode = 0x1BE;
    public const int ProgressSize = 112;
    public const int ProgressCapacity = 25;
    public const int ProgressCountOffset = 104;
    public const int ProgressDatabaseOffset = 108;
    public const uint ProgressIncrement = 0x01000000;
    public const int EntrySize = 292;
    public const int EntryRuntimeWordOffset = 288;
    public const int DatabasePrefixSize = 24;
    public const int DatabaseRuntimeStride = 52;
    public const int HeaderRuntimeStride = 136;
    public const int JournalWireSize = 368;
    public const int JournalRuntimeStride = 372;
    public const int JournalRuntimeGapOffset = 12;
    public const int GrowthSlots = 32;
    public const uint CreatureQuestInsertedFlag = 0x200;
    public static IReadOnlyList<int> EntryCallbackOffsets { get; } = Array.AsReadOnly(new[] { 0x10c, 0x110, 0x114, 0x118, 0x11c });

    internal static void RequireIndex(uint index, int growth, int stride)
    {
        if (((ulong)index + (uint)growth) * (uint)stride > uint.MaxValue)
            throw new InvalidDataException("Gold quest index overflows the native vector allocation.");
    }
}
