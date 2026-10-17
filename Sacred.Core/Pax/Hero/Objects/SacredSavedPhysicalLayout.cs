namespace Sacred.Core.Pax.Hero.Objects;

/// <summary>Gold 0x44FC50: 192 file bytes follow the base; no physical marker is written.</summary>
public static class SacredSavedPhysicalLayout
{
    public const int Size = 247;
    public const int Word64Offset = 55;
    public const int WordF0Offset = 59;
    public const int Word60Offset = 63;
    public const int Word68Offset = 67;
    public const int Word6COffset = 71;
    public const int Word5COffset = 75;
    /// <summary>Native physical +0x70 direction vector. Gold 0x450630 projects X/Y through
    /// 0x622620 and atan2 to obtain the movement-end heading; distinct from base +0x28 storage.</summary>
    public const int Vector70Offset = 79;
    public const int VectorB4Offset = 91;
    public const int VectorC0Offset = 103;
    public const int VectorCCOffset = 115;
    public const int VectorD8Offset = 127;
    public const int VectorE4Offset = 139;
    public const int Word94Offset = 151;
    public const int Word96Offset = 153;
    public const int WordB0Offset = 155;
    public const int ReservedWordsOffset = 159;
    public const int AnimationDoublesOffset = 175;
    public const int BoneVectorOffset = 199;
    public const int BoneBlockOffset = 211;
}
