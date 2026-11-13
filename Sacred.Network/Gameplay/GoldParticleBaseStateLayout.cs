namespace Sacred.Network.Gameplay;

/// <summary>Full-object base particle stream from 0x768EA0. Names containing
/// native offsets retain unresolved field semantics; ranges are actual copies,
/// not inferred struct padding. Native words +0x68..0x73 are not transmitted.</summary>
public static class GoldParticleBaseStateLayout
{
    public const int Size = 160;
    public const int ResourceOffset = 4; // Native +0x04.
    public const int PositionOffset = 8; // Native +0x08, three floats.
    public const int Native14BlockOffset = 20; // Native +0x14, 36 bytes.
    public const int Native38VectorsOffset = 56; // Three consecutive 12-byte vectors.
    public const int Native5CVectorOffset = 92;
    public const int Native74WordOffset = 104;
    public const int Native78Offset = 106;
    public const int Native7COffset = 110;
    public const int Native80Offset = 114;
    public const int Native84Offset = 118;
    public const int Native88Offset = 122;
    public const int Native8CVectorOffset = 126;
    public const int Native98Offset = 138;
    public const int CapacityOffset = 142; // Native +0x9C.
    public const int NativeA0Offset = 146;
    public const int NativeA4Offset = 150;
    public const int NativeA8ByteOffset = 154;
    public const int NativeA9ByteOffset = 155;
    public const int ObjectReferenceOffset = 156; // Native +0xAC.
}
