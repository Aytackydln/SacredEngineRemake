using System.Runtime.InteropServices;
using Sacred.Core.Binary;

namespace Sacred.Particles.Particles;

/// <summary>Native sprite-flare control fields outside the serialized subclass state.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = 0xA8)]
public struct SacredSpriteFlareObjectInputsLayout
{
    public const ushort BfgRemovalMask = 0x0002;
    public const ushort PillRemovalMask = 0x0008;
    [FieldOffset(0x74)] public ushort Flags;
    [FieldOffset(0x7C)] public uint Notification;
    [FieldOffset(0x98), BinaryUnknown] public uint Auxiliary;
    [FieldOffset(0x9C)] public uint LightStrength;
    /// <summary>BFG's ground glow receives local Z offset equal to the negative of this field.</summary>
    [FieldOffset(0xA4)] public float GroundFlareHeightOffset;
}
