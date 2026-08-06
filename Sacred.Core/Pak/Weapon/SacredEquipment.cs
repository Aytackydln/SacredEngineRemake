using System.Collections.Frozen;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using Sacred.Core.Pak.Items;

namespace Sacred.Core.Pak.Weapon;

[StructLayout(LayoutKind.Explicit, Pack = 1, Size = Size)]
internal readonly struct SacredEquipmentLayout
{
    public const int Size = 258;

    [FieldOffset(0)]
    public readonly ushort Short1;

    [FieldOffset(2)]
    public readonly float PreviewRotationX;

    [FieldOffset(6)]
    public readonly float PreviewRotationY;

    [FieldOffset(10)]
    public readonly float PreviewRotationZ;

    [FieldOffset(26)]
    public readonly byte Width;

    [FieldOffset(27)]
    public readonly byte Height;

    [FieldOffset(28)]
    public readonly byte UsageIdentifier;

    [FieldOffset(37)]
    public readonly byte TypeIdentifier;

    [FieldOffset(126)]
    public readonly ushort ItemId;

    [FieldOffset(130)]
    public readonly byte CharacterClassMaskCode;

    [FieldOffset(131)]
    public readonly byte EquipmentTypeCode;

    [FieldOffset(132)]
    public readonly byte RarityAndClassFlags;

    [FieldOffset(154)]
    public readonly ushort PhysicalDamageMinimum;

    [FieldOffset(156)]
    public readonly ushort FireDamageMinimum;

    [FieldOffset(158)]
    public readonly ushort MagicDamageMinimum;

    [FieldOffset(160)]
    public readonly ushort PoisonDamageMinimum;

    [FieldOffset(162)]
    public readonly ushort PhysicalDamageMaximum;

    [FieldOffset(164)]
    public readonly ushort FireDamageMaximum;

    [FieldOffset(166)]
    public readonly ushort MagicDamageMaximum;

    [FieldOffset(168)]
    public readonly ushort PoisonDamageMaximum;
}

// each entry is 258 bytes, with some fields at fixed offsets
// debug view with ItemId, Name, Width, Height, TypeIdentifier
[DebuggerDisplay("{IdemId}: {Name}, Class = {EffectiveCharacterClassMask}, Type = {EquipmentType}, RarityTier = {RarityTier}")]
public readonly record struct SacredEquipment(
    ItemsPakEntry Item,
    ushort Short1, // 2 bytes at offset 0
    Vector3 PreviewRotation, // candidate item preview rotation: three unaligned floats at offsets 2, 6, and 10 in radians
    byte Width, // 1 byte at offset 26
    byte Height, // 1 byte at offset 27
    byte UsageIdentifier, // 1 byte at offset 28; weapon/animation shape, partly tied to handedness
    byte TypeIdentifier, // 1 byte at offset 37; observed as 0 in sampled equipment
    string Name, // 88 bytes at offset 38-125, null-terminated string in iso 8859-1 encoding
    uint IdemId, // 2 bytes at offset 126, kept as uint for existing dictionary keys
    SacredEquipmentClassification Classification,
    SacredEquipmentDamage Damage
)
{
    // iso 8859-1 encoding for german text
    private static readonly Encoding SacredEncoding = Encoding.GetEncoding("iso-8859-1");

    public byte CharacterClassMaskCode => Classification.CharacterClassMaskCode;
    public SacredCharacterClassMask CharacterClassMask => Classification.CharacterClassMask;
    public SacredCharacterClassMask EffectiveCharacterClassMask => Classification.EffectiveCharacterClassMask;
    public byte EquipmentTypeCode => Classification.EquipmentTypeCode;
    public SacredEquipmentType EquipmentType => Classification.EquipmentType;
    public byte RarityAndClassFlags => Classification.RarityAndClassFlags;
    public byte RarityTierCode => Classification.RarityTierCode;
    public SacredEquipmentRarityTier RarityTier => Classification.RarityTier;
    public byte ClassFlagCode => Classification.ClassFlagCode;
    public SacredEquipmentLore InferredLore => Classification.InferLore(Height);
    public SacredEquipmentHandedness InferredHandedness => Classification.InferHandedness(UsageIdentifier, Height);

    public bool? InferredTwoHanded => InferredHandedness switch
    {
        SacredEquipmentHandedness.TwoHanded => true,
        SacredEquipmentHandedness.OneHanded or SacredEquipmentHandedness.NotApplicable => false,
        _ => null
    };

    public static SacredEquipment FromBytes(
        BinaryReader br,
        FrozenDictionary<ushort, ItemsPakEntry> items
    )
    {
        Span<byte> bytes = stackalloc byte[SacredEquipmentLayout.Size];
        br.BaseStream.ReadExactly(bytes);

        var nameBytes = bytes[38..126];
        var nullIndex = nameBytes.IndexOf((byte)0);

        var name = SacredEncoding.GetString(nameBytes[..nullIndex]);
        var layout = MemoryMarshal.Read<SacredEquipmentLayout>(bytes);

        var itemId = layout.ItemId;
        var item = items[itemId];
        var classification = SacredEquipmentClassification.FromBytes(
            characterClassMaskCode: layout.CharacterClassMaskCode,
            equipmentTypeCode: layout.EquipmentTypeCode,
            rarityAndClassFlags: layout.RarityAndClassFlags
        );

        return new SacredEquipment(Item: item,
            Short1: layout.Short1,
            PreviewRotation: new Vector3(
                layout.PreviewRotationX,
                layout.PreviewRotationY,
                layout.PreviewRotationZ
            ),
            Width: layout.Width,
            Height: layout.Height,
            UsageIdentifier: layout.UsageIdentifier,
            TypeIdentifier: layout.TypeIdentifier,
            Name: name,
            IdemId: itemId,
            Classification: classification,
            Damage: new SacredEquipmentDamage(
                Physical: new SacredDamageRange(layout.PhysicalDamageMinimum, layout.PhysicalDamageMaximum),
                Fire: new SacredDamageRange(layout.FireDamageMinimum, layout.FireDamageMaximum),
                Magic: new SacredDamageRange(layout.MagicDamageMinimum, layout.MagicDamageMaximum),
                Poison: new SacredDamageRange(layout.PoisonDamageMinimum, layout.PoisonDamageMaximum))
        );
    }
}
