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
    public const uint ModelLoadedFlag = 0x04000000;
    [FieldOffset(0), BinaryUnknown] public readonly uint Marker;
    /// <summary>Native cNode +0x08 flags. Bit 0x80 is active; 0x640640
    /// sets it before activation callbacks, independently of model loading.
    /// File reader 449C19 uses only bit 0x80 to request activation; it does not
    /// replace the runtime node flags with this byte.</summary>
    [FieldOffset(4)] public readonly byte ProcessFlags;
    public bool NodeActive => (ProcessFlags & 0x80) != 0;
    /// <summary>Native +0x09 queue selector: Gold 0x640660 uses queue +0x34
    /// for value 2 and queue +0x40 otherwise. Ordinary creatures start with 1.
    /// File reader consumes this byte without assigning the runtime selector.</summary>
    [FieldOffset(5)] public readonly byte ProcessByte09;
    public byte NodeQueueKind => ProcessByte09;
    /// <summary>Writer 449AE8 masks native +0x04 to its LOW BYTE and writes
    /// that as a four-byte word. Reader 449C12 consumes/discards it; it does not
    /// replace the constructed runtime node period. Native +0x04 is otherwise
    /// the unsigned update period in milliseconds.
    /// 0x640733/0x640745 multiplies it by .001 for the next scheduled time.
    /// Ordinary creature constructors use 50 ms; ordinary item constructors
    /// (0x5BE130) use 200,000 ms.</summary>
    [FieldOffset(6)] public readonly uint ProcessWord04;
    public uint NodePeriodMilliseconds => ProcessWord04;
    [FieldOffset(10)] public readonly uint ObjectReference;
    [FieldOffset(14)] public readonly uint Resource;
    /// <summary>Native base +0x14 flags. Bit 0x10 marks an inactive/removed object;
    /// Gold removal 0x5FC3D3 sets it and receive/owner/movement paths reject that object.
    /// For containers, bit 0x4000 marks opened/broken (Gold 0x7DF309 / 0x5CA447).
    /// Bit 0x100000 is Flying (Gold SetupFly 0x44DED0 / flight-stop receive 0x806202).
    /// Bit 0x04000000 means a Granny model sequence is loaded (0x44C320),
    /// independently of the node's ProcessFlags active bit.
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
    /// <summary>Native +0x2C link (Gold 0x4498F0). Flags bit 0x400 selects an
    /// attachment parent; otherwise this is the world link. This is independent
    /// of the peer-owner byte at native +0x39. A positioned indoor creature uses
    /// the unflagged link for its Static.pak floor child (Gold 0x5FBA40/0x5FCCB0).</summary>
    [FieldOffset(37)] public readonly uint Word2C;
    public uint AttachmentParent => (Flags & 0x400) != 0 ? Word2C : 0;
    public uint WorldLink => (Flags & 0x400) == 0 ? Word2C : 0;
    /// <summary>Native +0x30 next object in the tile's dynamic world-object chain.
    /// Gold 0x5FDE10 links the previous WLDX tile +0x08 head here before replacing
    /// that head with this object reference; an empty/self head preserves this word.</summary>
    [FieldOffset(41)] public readonly uint Word30;
    public uint NextWorldObjectReference => Word30;
    /// <summary>Object trigger reference at native +0x34; assigned by Gold OBJECT_SET_TRIGGER (0x174).</summary>
    [FieldOffset(TriggerReferenceOffset)] public readonly uint Word34;
    public uint TriggerReference => Word34;
    /// <summary>Gold 0x5FB2DF copies the shared allocation-counter byte here after
    /// construction, before registry insertion/activation. 0x60ABD0 repeats this
    /// byte across a word and XORs protected creature-value snapshots with it.</summary>
    [FieldOffset(49)] public readonly byte Byte38;
    public byte ValueSnapshotMaskByte => Byte38;
    [FieldOffset(50)] public readonly byte Owner;
    /// <summary>Gold 0x44A1C0 reads native +0x3C as the instance script-resource ID;
    /// zero falls back to the Items.pak descriptor's +0x20 ResourceId.</summary>
    [FieldOffset(51)] public readonly uint Word3C;
    public uint ScriptResourceId => Word3C;
}
