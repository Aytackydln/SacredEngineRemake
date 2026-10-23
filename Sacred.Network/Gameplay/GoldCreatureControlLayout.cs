namespace Sacred.Network.Gameplay;

/// <summary>Gold CREATURE_UPDATE_FIGHTMODE (0x7DDAAB) and CREATURE_UPDATE_DLG (0x7DDB9F).</summary>
public static class GoldCreatureControlLayout
{
    public const ushort FightModeOpcode = 0x119;
    public const ushort DialogOpcode = 0x11C;
    public const int FightModeSize = 8;
    public const int DialogSize = 12;
    public const int ReferenceOffset = 0;
    public const int ValueOffset = 4;
    public const int ObjectFlagsOffset = 8;
}
