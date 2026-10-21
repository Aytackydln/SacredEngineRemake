using System.Runtime.InteropServices;

namespace Sacred.Particles.Particles;

/// <summary>Gold particle base fields consumed by cObjectFX, independent of a particle family's saved state.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = 0x98)]
public struct SacredParticleOwnerCommandsLayout
{
    /// <summary>Input notification bits; distinct from the output/attachment flags at +7C.</summary>
    [FieldOffset(0x74)] public ushort RequestFlags;
    [FieldOffset(0x78)] public uint RequestData;
    /// <summary>0x200 follows wearer bone and orientation, 0x1000 supplies three extra
    /// bone positions, 0x400000 follows the alternate multi-bone path. These do not add stats.</summary>
    [FieldOffset(0x7C)] public uint AttachmentAndUpdateFlags;
    /// <summary>Gold 0x7CBD90 consumes and clears this value. cObjectFX 0x5B2600
    /// executes command 6 as generic combat-art modifiers and 7 as a specialized owner operation.</summary>
    [FieldOffset(0x80)] public uint PendingCommand;
    [FieldOffset(0x84)] public uint CommandParameter1;
    [FieldOffset(0x88)] public uint CommandParameter2;
    [FieldOffset(0x8C)] public float CommandX;
    [FieldOffset(0x90)] public float CommandY;
    [FieldOffset(0x94)] public float CommandZ;
}
