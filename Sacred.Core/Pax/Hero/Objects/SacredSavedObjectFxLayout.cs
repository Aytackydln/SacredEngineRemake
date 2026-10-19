namespace Sacred.Core.Pax.Hero.Objects;

/// <summary>Gold cObjectFX file writer 0x5B6A50. This layer follows cObject, not cObject3D.</summary>
public static class SacredSavedObjectFxLayout
{
    public const uint Marker = 0xBAADC1FE;
    public const int MarkerOffset = SacredSavedObjectBaseLayout.Size;
    public const int FxTypeOffset = 59;
    public const int FlagsOffset = 63;
    public const int LifeOffset = 65;
    public const int CasterReferenceOffset = 67;
    public const int DestinationReferenceOffset = 71;
    public const int WearerReferenceOffset = 75;
    public const int ParticleReferenceOffset = 79;
    public const int AttachBoneOffset = 81;
    public const int AdditionalBonesOffset = 83; // Three UInt16 bone indices.
    public const int MoveDirectionOffset = 89;
    public const int DurationOffset = 101;
    public const int SystemSecondsOffset = 105;
    public const int WearingOffsetOffset = 109;
    public const int FloatDataOffset = 121; // Four float operands.
    public const int VectorData1Offset = 137;
    public const int VectorData2Offset = 149;
    public const int IntegerDataOffset = 161; // Three UInt32 operands.
    public const int EventDataOffset = 173; // Original 56-byte sEventFXData.
    public const int EventDataSize = 56;
    public const int RemainingRechargeOffset = 229;
    public const int ReferenceCountOffset = 233;
    public const int ReferencesOffset = 237; // UInt32 references, count at ReferenceCountOffset.
    public const int MinimumSize = ReferencesOffset;
}
