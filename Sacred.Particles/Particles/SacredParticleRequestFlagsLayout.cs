using System.Runtime.InteropServices;

namespace Sacred.Particles.Particles;

/// <summary>Native base-system requestFlags at Gold object +0x74. This is an object
/// field, outside the derived-state payload. Shaddar Magic 0x7AAAC0 tests EXPLODE;
/// 0x7AAACC..0x7AAAD2 consumes it before switching to its impact emission block.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = 4)]
public readonly struct SacredParticleRequestFlagsLayout
{
    public const int NativeObjectOffset = 0x74;
    /// <summary>PS_REQUEST_FLAG_* bit values from the processed Demo declarations.</summary>
    [FieldOffset(0)] public readonly uint Flags;
    public bool ExplodeRequested => (Flags & ExplodeMask) != 0;
    public const uint TerminateMask = 1, ExplodeMask = 2, NoMoveMask = 4,
        NotifyMask = 8, NotifySecondMask = 16;
}
