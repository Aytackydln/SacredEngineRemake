namespace Sacred.Network.Gameplay;

/// <summary>Gold's actual-quest table. The companion word's meaning remains unresolved.</summary>
public static class GoldActualQuestsLayout
{
    public const ushort ResetOpcode = 0x1C9;
    public const ushort FullOpcode = 0x1CA;
    public const int SlotCount = 16;
    public const int RecordSize = 8;
    public const int Size = SlotCount * RecordSize;
    public const int QuestWordOffset = 0;
    public const int CompanionWordOffset = 4;
    public const int BankSection = 8;
}
