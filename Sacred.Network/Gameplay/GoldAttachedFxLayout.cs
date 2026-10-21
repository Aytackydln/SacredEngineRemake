namespace Sacred.Network.Gameplay;

/// <summary>Gold 0x149, emitted by 0x7D8950 and consumed by 0x7E9BD0.
/// Creates a client-local effect; there is no transmitted FX object reference.</summary>
public static class GoldAttachedFxLayout
{
    public const ushort Opcode = 0x149;
    public const int Size = 28;
    public const int ResourceOffset = 0;
    public const int FxSubtypeOffset = 4;
    public const int WearerOffset = 8;
    public const int SelectorOffset = 12;
    public const int VectorOffset = 16;
}
