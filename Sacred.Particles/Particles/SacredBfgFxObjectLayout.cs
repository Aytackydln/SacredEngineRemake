using System.Runtime.InteropServices;

namespace Sacred.Particles.Particles;

/// <summary>Recovered BFG object tail: saved controller followed by four constructor resource handles.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = 0x20C4)]
public struct SacredBfgFxObjectLayout
{
    [FieldOffset(0)] public SacredSpriteFlareObjectInputsLayout Inputs;
    [FieldOffset(0x20A0)] public SacredBfgFxStateLayout State;
    [FieldOffset(0x20B4)] public uint GroundGlowTextureHandle;
    [FieldOffset(0x20B8)] public uint FirstBillboardTextureHandle;
    [FieldOffset(0x20BC)] public uint SecondBillboardTextureHandle;
    [FieldOffset(0x20C0)] public uint ThirdBillboardTextureHandle;
}
