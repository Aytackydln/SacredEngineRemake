using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Sacred.Particles.Particles;

/// <summary>256 packed AARRGGBB values built by 0x763F50, stored in the serialized
/// system state. Draw flag 0x08 selects [parameterSet * 256 + truncate(fade)].
/// Index 255 is birth; index 0 is the end of the fade. Without flag 0x08 only
/// the first four words are initialized and used as quad corner colors.</summary>
[InlineArray(EntryCount)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct SacredParticleColorTableLayout
{
    public const int EntryCount = 256;
    public const int SerializedSize = EntryCount * sizeof(uint);
    private uint _element0;
}
