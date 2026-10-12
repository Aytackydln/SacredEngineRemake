namespace Sacred.Network.Admission;

/// <summary>Server-to-client opcode 0x1B at 0x4D3333; client 0x7E4C20 requires 0x3762 bytes.</summary>
public static class GoldPlayerRosterLayout
{
    public const ushort Opcode = GoldPlayerDataLayout.Opcode;
    public const int EntryCount = 17;
    public const int EntryBytes = GoldPlayerDataLayout.Size;
    public const int Size = EntryCount * EntryBytes;
}
