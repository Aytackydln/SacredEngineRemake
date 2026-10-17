namespace Sacred.Network.Gameplay;

/// <summary>Native script variable vector: client interpreter +0x7550, GameServer global 0x5D3938.</summary>
public static class GoldScriptVariableLayout
{
    public const ushort AppendOpcode = 0x1C6;
    public const ushort UpdateOpcode = 0x1C7;
    public const ushort BankOpcode = GoldWorldBankLayout.Bank1Opcode;
    public const int BankSection = 3;
    public const int NameSize = 32;
    public const int ValueOffset = 32;
    public const int RecordSize = 36;
    public const int UpdateSize = 40;
    public const uint GrowthSlots = 32;
    public const uint MaximumUpdateIndex = uint.MaxValue / RecordSize - GrowthSlots;
}
