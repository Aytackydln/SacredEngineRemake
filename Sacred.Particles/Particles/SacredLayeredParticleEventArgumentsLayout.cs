using System.Runtime.InteropServices;
using Sacred.Core.Binary;

namespace Sacred.Particles.Particles;

/// <summary>Mapped extent of the initializer argument, not a claim about the full event size.
/// Time Control reads +38 as unsigned milliseconds; Small Fire reads its low byte as subtype.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = 0x3C)]
public readonly struct SacredLayeredParticleEventArgumentsLayout
{
    [FieldOffset(0x00)] public readonly uint NativeVtablePointer;
    /// <summary>cEventFX dispatch selector, not an Items.pak type ID (Gold 0x5A3A3B).</summary>
    [FieldOffset(0x04)] public readonly uint EffectEventType;
    [FieldOffset(0x08)] public readonly uint CasterReference;
    /// <summary>Time Control's cObjectFX handler copies this reference to its wearer at 0x5A57D5.</summary>
    [FieldOffset(0x0C)] public readonly uint DestinationReference;
    [FieldOffset(0x10), BinaryUnknown] public readonly ulong Unknown010;
    [FieldOffset(0x18), BinaryUnknown] public readonly ulong Unknown018;
    /// <summary>Position passed to setParticleSystemPosition by the shared event path at 0x5A3BEC.</summary>
    [FieldOffset(0x20)] public readonly SacredParticleVectorLayout Position;
    /// <summary>cEventFX direction; Dwarf Shoot Impact normalizes the same +2C vector.</summary>
    [FieldOffset(0x2C)] public readonly SacredParticleVectorLayout Direction;
    [FieldOffset(0x38)] public readonly uint TimeControlDurationMilliseconds;
    [FieldOffset(0x38)] public readonly byte SmallFireSubtype;
}
