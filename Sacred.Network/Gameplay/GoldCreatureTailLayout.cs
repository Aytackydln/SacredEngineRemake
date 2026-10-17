namespace Sacred.Network.Gameplay;

/// <summary>Gold 0x54BE92..0x54BF6F, following the embedded values writer.</summary>
public static class GoldCreatureTailLayout
{
    public const int Offset = GoldCreatureStateLayout.PrefixSize;
    public const int RecordSize = 9;
    public const int CountSize = 2;
    public const int TrailerSize = 11;
    public const int ModeOffset = 0;
    public const int Word570Offset = 1;
    public const int MountPartnerReferenceOffset = Word570Offset;
    public const int Word574Offset = 5;
    public const int Word578Offset = 7;
}
