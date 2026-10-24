using System.Runtime.InteropServices;

namespace Sacred.Particles.Particles;

/// <summary>HolyWall saved controller, native object +0x20A0, save 0x784AF0.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = 0x18)]
public readonly struct SacredHolyWallStateLayout
{
    [FieldOffset(0)] public readonly float Elapsed;
    [FieldOffset(4)] public readonly byte Active;
    [FieldOffset(8)] public readonly float Angle;
    [FieldOffset(0xC)] public readonly float HelixHeight;
    [FieldOffset(0x10)] public readonly float ActorRadius;
    [FieldOffset(0x14)] public readonly float Duration;
}

/// <summary>HolyLight saved controller, native object +0x20A0, save 0x7813E0.
/// Duration is saved separately from native +0x2148 after the standard particle blocks.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = 0x14)]
public readonly struct SacredHolyLightStateLayout
{
    [FieldOffset(0)] public readonly float Elapsed;
    [FieldOffset(4)] public readonly byte Active;
    [FieldOffset(8)] public readonly float BirthHalfSize;
    [FieldOffset(0xC)] public readonly float Brightness;
    [FieldOffset(0x10)] public readonly float Decay;
}
