namespace Sacred.Network.Gameplay;

/// <summary>Gold cObjectFX packet writer 0x5B6F80; offsets include the base cObject stream.</summary>
public static class GoldObjectFxStateLayout
{
    public const uint Marker = 0xBAADC1FE;
    public const int MarkerOffset = GoldObjectStateLayout.Size;
    public const int FxTypeOffset = 45; // Native +0x44, cEventFX subtype, not an Items.pak resource.
    public const int FlagsOffset = 49; // Native +0x48 sfxFlags.
    public const int LifeOffset = 51; // Native +0x50 sfxLife.
    public const int CasterReferenceOffset = 53;
    public const int CasterInstanceOffset = 57;
    public const int DestinationReferenceOffset = 58;
    public const int DestinationInstanceOffset = 62;
    public const int AttachBoneOffset = 63;
    public const int AdditionalBonesOffset = 65;
    public const int MoveDirectionOffset = 71;
    public const int DurationOffset = 83;
    public const int WearingOffsetOffset = 87;
    public const int FloatDataOffset = 99; // Native +0xA8..+0xB4, four float operands.
    public const int VectorData1Offset = 115;
    public const int VectorData2Offset = 127;
    public const int IntegerDataOffset = 139; // Native +0x9C..+0xA4, three integer operands.
    public const int EventDataOffset = 151;
    public const int EventDataSize = 56;
    public const int RemainingRechargeOffset = 207;
    public const int WordDcOffset = 211;
    public const int WordE0Offset = 215;
    public const int WearerReferenceOffset = 219;
    public const int ReferenceCountOffset = 223;
    public const int ReferencesOffset = 227;
    public const int MinimumSize = ReferencesOffset;
}
