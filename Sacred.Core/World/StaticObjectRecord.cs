using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Sacred.Core.Binary;

namespace Sacred.Core.World;

[InlineArray(8)]
public struct StaticObjectReservedBytes
{
    private byte _element0;
}

/// <summary>Known rendering and surface-selection bits in a Static.pak object record.</summary>
[Flags]
public enum StaticObjectFlags : uint
{
    None = 0,
    /// <summary>Unresolved flag bit. The name preserves its raw hexadecimal value.</summary>
    Byte00000001 = 0x0000_0001,
    /// <summary>Unresolved flag bit. The name preserves its raw hexadecimal value.</summary>
    Byte00000002 = 0x0000_0002,
    /// <summary>Unresolved flag bit. The name preserves its raw hexadecimal value.</summary>
    Byte00000004 = 0x0000_0004,
    AlternateSurface = 0x0000_0008,
    /// <summary>Unresolved flag bit used by Sacred.exe's normal-render exclusion test.</summary>
    Byte00000010 = 0x0000_0010,
    RearLayerBackground = 0x0000_0020,
    NightOnly = 0x0000_0040,
    /// <summary>Unresolved flag bit used by Sacred.exe's normal-render exclusion test.</summary>
    Byte00000080 = 0x0000_0080,
    /// <summary>Unresolved flag bit. The name preserves its raw hexadecimal value.</summary>
    Byte00000100 = 0x0000_0100,
    /// <summary>Unresolved flag bit used by Sacred.exe's normal-render exclusion test.</summary>
    Byte00000200 = 0x0000_0200,
    /// <summary>Unresolved flag bit. The name preserves its raw hexadecimal value.</summary>
    Byte00000400 = 0x0000_0400,
    /// <summary>Unresolved flag bit. The name preserves its raw hexadecimal value.</summary>
    Byte00000800 = 0x0000_0800,
    /// <summary>Unresolved flag bit. The name preserves its raw hexadecimal value.</summary>
    Byte00001000 = 0x0000_1000,
    /// <summary>Unresolved flag bit. The name preserves its raw hexadecimal value.</summary>
    Byte00002000 = 0x0000_2000,
    /// <summary>Unresolved flag bit. The name preserves its raw hexadecimal value.</summary>
    Byte00004000 = 0x0000_4000,
    /// <summary>Unresolved flag bit. The name preserves its raw hexadecimal value.</summary>
    Byte00008000 = 0x0000_8000,
    /// <summary>Unresolved flag bit. The name preserves its raw hexadecimal value.</summary>
    Byte00010000 = 0x0001_0000,
    /// <summary>Unresolved flag bit. The name preserves its raw hexadecimal value.</summary>
    Byte00020000 = 0x0002_0000,
    /// <summary>Unresolved flag bit. The name preserves its raw hexadecimal value.</summary>
    Byte00040000 = 0x0004_0000,
    /// <summary>Unresolved flag bit. The name preserves its raw hexadecimal value.</summary>
    Byte00080000 = 0x0008_0000,
    /// <summary>Unresolved flag bit. The name preserves its raw hexadecimal value.</summary>
    Byte00100000 = 0x0010_0000,
    /// <summary>Unresolved flag bit. The name preserves its raw hexadecimal value.</summary>
    Byte00200000 = 0x0020_0000,
    /// <summary>Unresolved flag bit. The name preserves its raw hexadecimal value.</summary>
    Byte00400000 = 0x0040_0000,
    /// <summary>Unresolved flag bit. The name preserves its raw hexadecimal value.</summary>
    Byte00800000 = 0x0080_0000,
    /// <summary>Unresolved flag bit. The name preserves its raw hexadecimal value.</summary>
    Byte01000000 = 0x0100_0000,
    /// <summary>Unresolved flag bit. The name preserves its raw hexadecimal value.</summary>
    Byte02000000 = 0x0200_0000,
    /// <summary>Unresolved flag bit. The name preserves its raw hexadecimal value.</summary>
    Byte04000000 = 0x0400_0000,
    /// <summary>Unresolved flag bit. The name preserves its raw hexadecimal value.</summary>
    Byte08000000 = 0x0800_0000,
    /// <summary>Unresolved flag bit. The name preserves its raw hexadecimal value.</summary>
    Byte10000000 = 0x1000_0000,
    /// <summary>Unresolved flag bit. The name preserves its raw hexadecimal value.</summary>
    Byte20000000 = 0x2000_0000,
    /// <summary>Unresolved flag bit. The name preserves its raw hexadecimal value.</summary>
    Byte40000000 = 0x4000_0000,
    /// <summary>Unresolved flag bit. The name preserves its raw hexadecimal value.</summary>
    Byte80000000 = 0x8000_0000,

    /// <summary>Composite mask of objects excluded from the normal static-sprite pass.</summary>
    NormalRenderExclusionMask = Byte00000010 | Byte00000080 | Byte00000200,
}

/// <summary>One linked static-world object record stored in <c>Static.pak</c>.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public readonly record struct StaticObjectRecord
{
    public const int SerializedSize = 0x40;

    /// <summary>Instance identifier stored in the payload.</summary>
    [FieldOffset(0x00)]
    public readonly uint PayloadInstanceId;

    /// <summary>
    /// Native <c>eItemType</c> value and Items.pak directory index selecting this object's visual definition.
    /// It is stored as UInt32 even though the installed archive has UInt16-addressable entries.
    /// </summary>
    [FieldOffset(0x04)]
    public readonly uint TypeId;

    /// <summary>
    /// Items.pak directory index for this object, when its native type value fits the archive's
    /// 16-bit entry index. The matching Gold static record with TypeId 12000 resolves to
    /// the Items.pak descriptor named <c>BDungeon1b.tga</c>.
    /// </summary>
    public ushort ItemsPakEntryIndex => checked((ushort)TypeId);

    /// <summary>Rendering and surface-selection flags.</summary>
    [FieldOffset(0x08)]
    public readonly StaticObjectFlags Flags;

    /// <summary>Native <c>sObjectWorldPosition</c> at the packed record offset 0x0C.</summary>
    [FieldOffset(0x0C)] public readonly WorldObjectPositionLayout Position;

    /// <summary>Owning sector identifier; aliases <see cref="Position"/>.SectorId.</summary>
    [FieldOffset(0x0C)] public readonly ushort SectorId;
    /// <summary>Projected world X coordinate; aliases <see cref="Position"/>.X.</summary>
    [FieldOffset(0x0E)] public readonly int ProjectedX;
    /// <summary>Projected world Y coordinate; aliases <see cref="Position"/>.Y.</summary>
    [FieldOffset(0x12)] public readonly int ProjectedY;
    /// <summary>Height level stored in the packed world position; aliases <see cref="Position"/>.ElevationTier.</summary>
    [FieldOffset(0x16)] public readonly byte ElevationTier;

    /// <summary>
    /// Native <c>sObject_Q</c> union: signed quality in its low word, quantity,
    /// or content identifier depending on object type.
    /// </summary>
    [FieldOffset(0x17)]
    public readonly int QualityQuantityOrContent;

    /// <summary>Native sObject_Q.quality: signed low word of the union at 0x17.</summary>
    [FieldOffset(0x17)] public readonly short Quality;
    /// <summary>Native sObject_Q.quantity: full signed word at 0x17.</summary>
    [FieldOffset(0x17)] public readonly int Quantity;
    /// <summary>Native sObject_Q.content: full signed word at 0x17.</summary>
    [FieldOffset(0x17)] public readonly int Content;

    /// <summary>Identifier of the container holding this object.</summary>
    [FieldOffset(0x1B)]
    public readonly uint ContainerId;

    /// <summary>Identifier of the next static object in the tile's linked list.</summary>
    [FieldOffset(0x1F)]
    public readonly uint NextStaticId;

    /// <summary>Horizontal per-instance anchor adjustment.</summary>
    [FieldOffset(0x23)]
    public readonly short AnchorOffsetX;

    /// <summary>Vertical per-instance anchor adjustment.</summary>
    [FieldOffset(0x25)]
    public readonly short AnchorOffsetY;

    /// <summary>Trigger identifier attached to this static object.</summary>
    [FieldOffset(0x27)]
    public readonly uint TriggerId;

    /// <summary>
    /// Current trigger state. For building parts with flag 0x08 this selects
    /// the visible surface: 1 exterior, 2 ground floor, 4 next floor, etc.
    /// Sacred compares it to the parent building trigger's active state.
    /// </summary>
    [FieldOffset(0x2B)]
    public readonly ushort TriggerState;

    /// <summary>Signed rendering interpretation of the state at 0x2B.</summary>
    [FieldOffset(0x2B)]
    public readonly short SurfaceVisibilityState;

    /// <summary>Additional height-level identifier outside the packed world position.</summary>
    [FieldOffset(0x2D)]
    public readonly byte HeightLevelId;

    /// <summary>First texture-region or animation selector byte.</summary>
    [FieldOffset(0x2E)]
    public readonly byte SpriteParam2E;

    /// <summary>Native miniObjU; aliases the existing sprite selector at 0x2E.</summary>
    [FieldOffset(0x2E)] public readonly byte MiniObjectU;

    /// <summary>Second texture-region or animation selector byte.</summary>
    [FieldOffset(0x2F)]
    public readonly byte SpriteParam2F;

    /// <summary>Native miniObjV; aliases the existing sprite selector at 0x2F.</summary>
    [FieldOffset(0x2F)] public readonly byte MiniObjectV;

    /// <summary>Sprite orientation or initial animation frame.</summary>
    [FieldOffset(0x30)]
    public readonly byte OrientationOrFrame;

    /// <summary>Native miniObjS; aliases the existing orientation/frame byte at 0x30.</summary>
    [FieldOffset(0x30)] public readonly byte MiniObjectS;

    /// <summary>Duration of one animation frame in game ticks.</summary>
    [FieldOffset(0x31)]
    public readonly byte AnimationFrameDurationTicks;

    /// <summary>Number of frames in the object's sprite animation.</summary>
    [FieldOffset(0x32)]
    public readonly byte AnimationFrameCount;

    /// <summary>
    /// Geometric height layer, scaled by 28 in Sacred's renderer. This is not
    /// the visibility selector; surface visibility uses the state at 0x2B.
    /// </summary>
    [FieldOffset(0x33)]
    public readonly byte SurfaceRenderLayer;

    /// <summary>Initial trigger state byte.</summary>
    [FieldOffset(0x34)]
    public readonly byte TriggerInitialState;

    /// <summary>Visibility-state mask for attached effects.</summary>
    [FieldOffset(0x35)]
    public readonly byte EffectVisibleStates;

    [FieldOffset(0x36)]
    [BinaryUnknown]
    public readonly byte Reserved1;

    /// <summary>Per-instance brightness.</summary>
    [FieldOffset(0x37)]
    public readonly byte Brightness;

    [FieldOffset(0x38)]
    [BinaryUnknown]
    public readonly StaticObjectReservedBytes Reserved;
}
