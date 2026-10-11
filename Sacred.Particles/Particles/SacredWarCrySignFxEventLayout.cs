using System.Runtime.InteropServices;

namespace Sacred.Particles.Particles;

/// <summary>The initializer copies event +48 directly into the saved lifetime.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = 0x4C)]
public struct SacredWarCrySignFxEventLayout
{
    [FieldOffset(0x48)] public float Lifetime;
}
