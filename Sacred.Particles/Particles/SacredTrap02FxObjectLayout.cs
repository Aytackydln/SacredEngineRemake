using System.Runtime.InteropServices;

namespace Sacred.Particles.Particles;

/// <summary>Both Trap02 visual objects save eight controller bytes and bind one billboard texture.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = 0x20AC)]
public struct SacredTrap02FxObjectLayout
{
    [FieldOffset(0)] public SacredSpriteFlareObjectInputsLayout Inputs;
    [FieldOffset(0x20A0)] public SacredTrap02FxStateLayout State;
    [FieldOffset(0x20A8)] public uint BillboardTextureHandle;
}
