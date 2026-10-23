using System.Runtime.InteropServices;
using Sacred.Core.Binary;

namespace Sacred.Core.Pax.Hero.Objects;

/// <summary>Gold file method 0x449AA0; this 55-byte stream differs from the 41-byte packet prefix.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = Size)]
public readonly struct SacredSavedObjectBaseLayout
{
    public const int Size = 55;
    public const int ObjectReferenceOffset = 10;
    public const int TriggerReferenceOffset = 45;
    [FieldOffset(0), BinaryUnknown] public readonly uint Marker;
    [FieldOffset(4)] public readonly byte ProcessFlags;
    [FieldOffset(5), BinaryUnknown] public readonly byte ProcessByte09;
    [FieldOffset(6), BinaryUnknown] public readonly uint ProcessWord04;
    [FieldOffset(10)] public readonly uint ObjectReference;
    [FieldOffset(14)] public readonly uint Resource;
    /// <summary>Native base +0x14 flags. Bit 0x10 marks an inactive/removed object;
    /// Gold removal 0x5FC3D3 sets it and receive/owner/movement paths reject that object.
    /// For containers, bit 0x4000 marks opened/broken (Gold 0x7DF309 / 0x5CA447).
    /// Bit 0x100000 is Flying (Gold SetupFly 0x44DED0 / flight-stop receive 0x806202).
    /// Bit 0x80000 gates the dialog overhead; dialog setter 0x5498F0 clears it for nonpositive selectors.</summary>
    [FieldOffset(18)] public readonly uint Flags;
    public bool IsInactive => (Flags & 0x10) != 0;
    /// <summary>Only meaningful when the original resource category selects cContainer.</summary>
    public bool IsContainerActivated => (Flags & 0x4000) != 0;
    [FieldOffset(22)] public readonly ushort Sector;
    [FieldOffset(24)] public readonly int X;
    [FieldOffset(28)] public readonly int Y;
    [FieldOffset(32)] public readonly byte Surface;
    [FieldOffset(33)] public readonly uint Word28;
    /// <summary>Native cached +0x28 angle in projected-world degrees; wire/file words preserve its float bits.
    /// Current physical heading is derived from +0x70 and can differ after movement.</summary>
    [FieldOffset(33)] public readonly float FacingDegrees;
    [FieldOffset(37), BinaryUnknown] public readonly uint Word2C;
    [FieldOffset(41), BinaryUnknown] public readonly uint Word30;
    /// <summary>Object trigger reference at native +0x34; assigned by Gold OBJECT_SET_TRIGGER (0x174).</summary>
    [FieldOffset(TriggerReferenceOffset)] public readonly uint Word34;
    public uint TriggerReference => Word34;
    /// <summary>Gold 0x60ABD0 repeats this byte across a word and XORs protected creature-value snapshots with it.</summary>
    [FieldOffset(49)] public readonly byte Byte38;
    public byte ValueSnapshotMaskByte => Byte38;
    [FieldOffset(50)] public readonly byte Owner;
    /// <summary>Gold 0x44A1C0 reads native +0x3C as the instance script-resource ID;
    /// zero falls back to the Items.pak descriptor's +0x20 ResourceId.</summary>
    [FieldOffset(51)] public readonly uint Word3C;
    public uint ScriptResourceId => Word3C;
}
