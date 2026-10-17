namespace Sacred.Network.Gameplay;

/// <summary>Gold 0x450140 physical stream; offsets include the 41-byte base object block.</summary>
public static class GoldPhysicalStateLayout
{
    public const int Size = 121;
    public const int MarkerOffset = GoldObjectStateLayout.Size;
    public const uint Marker = 0xF00DFACE;
    public const int Word64Offset = 45;
    /// <summary>cObject3D +0x64 current motion selector; 0xAA activates and 0xAB resets
    /// an interactive world model (Gold 0x44BEBB/0x44BF20).</summary>
    public const int AnimationIdOffset = Word64Offset;
    public const int WordF0Offset = 49;
    public const int Word60Offset = 53;
    public const int Word68Offset = 57;
    public const int Word6COffset = 61;
    /// <summary>Native physical direction vector +0x70; Gold 0x450630 derives the stop heading from its projected X/Y.</summary>
    public const int Vector70Offset = 65;
    public const int VectorD8Offset = 77;
    public const int VectorE4Offset = 89;
    public const int Word94Offset = 101;
    public const int Word96Offset = 103;
    public const int WordB0Offset = 105;
    public const int SelectedVectorOffset = 109;
    // Flag 0x800000 selects native +0xCC. Otherwise +0x98 or a Granny bone result is written.
    public const uint VectorCCFlag = 0x800000;
}
