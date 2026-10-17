namespace Sacred.Network.Gameplay;

/// <summary>Gold 0x148, consumed at 0x7E56E0. The caller requires exactly 62 payload bytes.</summary>
public static class GoldSectorEntryLayout
{
    public const ushort Opcode = 0x148;
    public const int Size = 62;
    public const int SlotCount = 9;
    public const int Word0Offset = 0;
    public const int SectorsOffset = 4;
    public const int RetainedWordsOffset = 22;
    public const int ScriptModesOffset = 40;
    public const int InitializeFlagsOffset = 49;
    public const int Word58Offset = 58;
}
