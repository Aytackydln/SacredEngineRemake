using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Sacred.Core.Binary;

namespace Sacred.Particles.Particles;

/// <summary>Gold serializer 0x775C40 transfers 0x510 bytes from object +0x20A0.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public readonly struct SacredFireCircleParticleStateLayout
{
    public const int SerializedSize = 0x510;
    [FieldOffset(0x000)] public readonly float ElapsedTime;
    [FieldOffset(0x004)] public readonly float Lifetime;
    /// <summary>Decreased by dt; reset to the quality-scaled 0.1 while emitting. No birth dependency.</summary>
    [FieldOffset(0x008)] public readonly float AuxiliaryCountdown;
    [FieldOffset(0x00C)] public readonly byte Active;
    [FieldOffset(0x00D), BinaryUnknown] public readonly byte Unknown00D;
    [FieldOffset(0x00E), BinaryUnknown] public readonly ushort Unknown00E;
    /// <summary>Copied from event +0x20/+0x24/+0x28; not consumed by this updater/draw.</summary>
    [FieldOffset(0x010)] public readonly SacredParticleVectorLayout EventVector;
    [FieldOffset(0x01C), BinaryUnknown] public readonly ulong Unknown01C;
    [FieldOffset(0x024)] public readonly SacredParticleColorTableLayout Colors;
    [FieldOffset(0x424)] public readonly SacredParticleMotionLayout Motion;
    [FieldOffset(0x444)] public readonly SacredParticleEmissionLayout Emission;
    [FieldOffset(0x4A8), BinaryUnknown] public readonly UnknownBytes Unmapped;
    /// <summary>Zero emits; one drains until stdMovement reports no live particles.</summary>
    [FieldOffset(0x50C)] public readonly uint EmissionStopped;

    [InlineArray(0x64)]
    public struct UnknownBytes { private byte _element0; }
}
