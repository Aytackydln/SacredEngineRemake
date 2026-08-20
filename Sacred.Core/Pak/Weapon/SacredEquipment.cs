using System.Collections.Frozen;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using Sacred.Core.Binary;
using Sacred.Core.Pak.Items;

namespace Sacred.Core.Pak.Weapon;

/// <summary>Fixed-size equipment record stored after the Weapon.pak header.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = Size)]
public readonly struct SacredEquipmentLayout
{
    public const int Size = 258;
    public const int NameOffset = 38;
    public const int NameLength = 88;

    /// <summary>Unresolved two-byte value at the beginning of the record.</summary>
    [FieldOffset(0)]
    [BinaryUnknown]
    public readonly ushort Short1;

    /// <summary>Item-preview rotation around the X axis, in radians.</summary>
    [FieldOffset(2)]
    public readonly float PreviewRotationX;

    /// <summary>Item-preview rotation around the Y axis, in radians.</summary>
    [FieldOffset(6)]
    public readonly float PreviewRotationY;

    /// <summary>Item-preview rotation around the Z axis, in radians.</summary>
    [FieldOffset(10)]
    public readonly float PreviewRotationZ;

    /// <summary>Inventory-grid width in cells.</summary>
    [FieldOffset(26)]
    public readonly byte Width;

    /// <summary>Inventory-grid height in cells.</summary>
    [FieldOffset(27)]
    public readonly byte Height;

    /// <summary>Weapon or animation usage code, partly associated with handedness.</summary>
    [FieldOffset(28)]
    public readonly byte UsageIdentifier;

    /// <summary>Equipment type byte observed as zero in the sampled records.</summary>
    [FieldOffset(37)]
    public readonly byte TypeIdentifier;

    /// <summary>Null-terminated equipment name encoded as ISO-8859-1.</summary>
    [FieldOffset(NameOffset)]
    [BinaryString("Name", NameLength, "ISO-8859-1")]
    private readonly byte _name;

    /// <summary>Items.pak identifier for the equipment's visual definition.</summary>
    [FieldOffset(126)]
    public readonly ushort ItemId;

    /// <summary>Encoded character-class availability mask.</summary>
    [FieldOffset(130)]
    public readonly byte CharacterClassMaskCode;

    /// <summary>Encoded equipment category.</summary>
    [FieldOffset(131)]
    public readonly byte EquipmentTypeCode;

    /// <summary>Packed rarity-tier and class-specific flags.</summary>
    [FieldOffset(132)]
    public readonly byte RarityAndClassFlags;

    /// <summary>Minimum physical damage.</summary>
    [FieldOffset(154)]
    public readonly ushort PhysicalDamageMinimum;

    /// <summary>Minimum fire damage.</summary>
    [FieldOffset(156)]
    public readonly ushort FireDamageMinimum;

    /// <summary>Minimum magic damage.</summary>
    [FieldOffset(158)]
    public readonly ushort MagicDamageMinimum;

    /// <summary>Minimum poison damage.</summary>
    [FieldOffset(160)]
    public readonly ushort PoisonDamageMinimum;

    /// <summary>Maximum physical damage.</summary>
    [FieldOffset(162)]
    public readonly ushort PhysicalDamageMaximum;

    /// <summary>Maximum fire damage.</summary>
    [FieldOffset(164)]
    public readonly ushort FireDamageMaximum;

    /// <summary>Maximum magic damage.</summary>
    [FieldOffset(166)]
    public readonly ushort MagicDamageMaximum;

    /// <summary>Maximum poison damage.</summary>
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
    string Name, // decoded after layout cast from the null-terminated 88-byte field at offset 38
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

        var layout = MemoryMarshal.Cast<byte, SacredEquipmentLayout>(bytes)[0];
        var name = ReadName(bytes);

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

    private static string ReadName(ReadOnlySpan<byte> bytes)
    {
        var nameBytes = bytes.Slice(SacredEquipmentLayout.NameOffset, SacredEquipmentLayout.NameLength);
        var nullIndex = nameBytes.IndexOf((byte)0);
        return SacredEncoding.GetString(nullIndex < 0 ? nameBytes : nameBytes[..nullIndex]);
    }
}
