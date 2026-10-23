namespace Sacred.Network.Gameplay;

/// <summary>Gold cObject3D quest setters: reference followed by a 32-bit value narrowed to signed 16 bits.</summary>
public static class GoldObjectQuestLayout
{
    public const ushort NumberOpcode = 0x173;
    public const ushort EffectOpcode = 0x175;
    public const int Size = 8;
    public const int ReferenceOffset = 0;
    public const int ValueOffset = 4;
}
