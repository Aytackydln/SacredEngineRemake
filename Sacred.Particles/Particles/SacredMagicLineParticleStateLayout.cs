using System.Runtime.InteropServices;

namespace Sacred.Particles.Particles;

/// <summary>Magic Fire/Gift serialize 0xA4 bytes from +0x20A0 (0x77A730/0x77C230).
/// The texture handle at +0x2144 follows this serialized block.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public readonly struct SacredMagicLineParticleStateLayout
{
    public const int SerializedSize = 0xA4;
    [FieldOffset(0)] public readonly SacredTorchSmokeParticleStateLayout Emitter;
    /// <summary>Unsigned event +0x3C scaled by the initializer; controls birth size and size growth.</summary>
    [FieldOffset(0xA0)] public readonly float Strength;
}
