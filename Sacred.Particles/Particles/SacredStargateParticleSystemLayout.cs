using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Sacred.Particles.Particles;

[InlineArray(16)]
public struct SacredStargateMinimapSlots { private uint _element0; }

/// <summary>Original 0x77FBE0 stargate particle object; 0x10D60-byte allocation.
/// Texture names/slots are loaded by the original executable, independently of
/// its serialized +0x20A0 block. Remaining particle fields are not mapped here.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public readonly struct SacredStargateParticleSystemLayout
{
    public const int SerializedSize = 0x10D60;
    [FieldOffset(0)] public readonly uint NativeVtableAddress;
    [FieldOffset(0x20A0)] public readonly SacredStargateParticleStateLayout State;
    [FieldOffset(0x10D18)] public readonly SacredStargateMinimapSlots MinimapTextureHandles;
    [FieldOffset(0x10D58)] public readonly uint Whirl01TextureHandle;
    [FieldOffset(0x10D5C)] public readonly uint Whirl02TextureHandle;
}
