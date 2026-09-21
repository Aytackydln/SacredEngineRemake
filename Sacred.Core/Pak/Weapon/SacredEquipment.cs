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
    public const int NameOffset = 40;
    public const int NameLength = 64;

    /// <summary>Uniform inventory-preview scale; Demo <c>TypeManager::getInventoryWorldMatrix</c> passes this to its scale-matrix helper.</summary>
    [FieldOffset(0)]
    public readonly float PreviewScale;

    /// <summary>Item-preview rotation around the X axis, in radians. Demo composes the preview as scale * Rx * Ry * Rz.</summary>
    [FieldOffset(4)]
    public readonly float PreviewRotationX;

    /// <summary>Item-preview rotation around the Y axis, in radians.</summary>
    [FieldOffset(8)]
    public readonly float PreviewRotationY;

    /// <summary>Item-preview rotation around Z in native radians; negate for System.Numerics.CreateRotationZ.</summary>
    [FieldOffset(12)]
    public readonly float PreviewRotationZ;

    /// <summary>Native preview translation X, added to matrix +0x30 by Demo <c>TypeManager::getInventoryWorldMatrix</c>.</summary>
    [FieldOffset(16)]
    public readonly float PreviewOffsetX;

    /// <summary>sWeaponInfoShared::ty; stored Y translation, ignored by the inventory world-matrix function.</summary>
    [FieldOffset(20)] public readonly float PreviewOffsetY;

    /// <summary>Native preview translation Z, added to matrix +0x38 by Demo <c>TypeManager::getInventoryWorldMatrix</c>.</summary>
    [FieldOffset(24)]
    public readonly float PreviewOffsetZ;

    /// <summary>Inventory-grid width in cells.</summary>
    [FieldOffset(28)]
    public readonly byte Width;

    /// <summary>Inventory-grid height in cells.</summary>
    [FieldOffset(29)]
    public readonly byte Height;

    /// <summary>Weapon or animation usage code, partly associated with handedness.</summary>
    [FieldOffset(30)]
    public readonly byte UsageIdentifier;

    /// <summary>Native sWeaponInfoShared event-set selectors; names preserved from the demo.</summary>
    [FieldOffset(31)] public readonly byte HitSet;
    [FieldOffset(32)] public readonly byte ParrySet;
    [FieldOffset(33)] public readonly byte MissSet;
    [FieldOffset(34)] public readonly byte LolSet;
    [FieldOffset(35)] public readonly byte SetType;

    /// <summary>Optional base visual item. Native predicates test this ID after the item's own ID;
    /// 0x425A35 also copies its Items.pak descriptor. Zero means no inherited visual.</summary>
    [FieldOffset(36)]
    public readonly uint BaseItemId;

    /// <summary>Null-terminated equipment name encoded as ISO-8859-1.</summary>
    [FieldOffset(NameOffset)]
    [BinaryString("Name", NameLength, "ISO-8859-1")]
    private readonly byte _name;

    /// <summary>sWeaponInfoShared::slotDefault[6], immediately after Name[64].
    /// These are six 32-bit eItemType IDs, not part of the name string.</summary>
    [FieldOffset(0x68)] public readonly SacredEquipmentDefaultSlots DefaultSlotItems;

    /// <summary>Items.pak identifier for the equipment's visual definition.</summary>
    [FieldOffset(128)]
    public readonly uint ItemId;

    /// <summary>Native <c>sWeaponInfo::Flag</c> word, retaining every authored flag bit.</summary>
    [FieldOffset(132)]
    public readonly uint RawFlags;

    /// <summary>Character-class availability flags.</summary>
    [FieldOffset(132)]
    public readonly SacredCharacterClassMask CharacterClassMask;

    /// <summary>Equipment category.</summary>
    [FieldOffset(133)]
    public readonly SacredEquipmentType EquipmentType;

    /// <summary>Packed rarity-tier and class-specific flags.</summary>
    [FieldOffset(134)]
    public readonly byte RarityAndClassFlags;

    /// <summary>Remaining high byte of the native sWeaponInfo::Flag word.</summary>
    [FieldOffset(135), BinaryUnknown] public readonly byte UnknownFlagHighByte;
    [FieldOffset(136)] public readonly uint Price;
    /// <summary>Native SlotType[8] bytes.</summary>
    [FieldOffset(140)] public readonly SacredEquipmentSlotTypes SlotTypes;
    [FieldOffset(148)] public readonly byte MinimumLevel;
    [FieldOffset(149)] public readonly byte MinimumStrength;
    [FieldOffset(150)] public readonly byte MinimumDexterity;
    [FieldOffset(151)] public readonly byte MinimumCharisma;
    /// <summary>Native MinWiederstand; original spelling retained in the symbol catalogue.</summary>
    [FieldOffset(152)] public readonly byte MinimumResistance;
    [FieldOffset(153)] public readonly byte SpawnLevel;
    [FieldOffset(154)] public readonly byte MinimumSkill;
    [FieldOffset(155)] public readonly byte MinimumSkillLevel;

    /// <summary>Min & max damage values for each damage type.</summary>
    [FieldOffset(156)]
    public readonly SacredEquipmentDamage EquipmentDamage;

    /// <summary>Native AW, PW, BW values; signed 16-bit fields in sWeaponInfo.</summary>
    [FieldOffset(172)] public readonly short AttackValue;
    [FieldOffset(174)] public readonly short ParryValue;
    [FieldOffset(176)] public readonly short BW;
    [FieldOffset(178)] public readonly short PhysicalResistance;
    [FieldOffset(180)] public readonly short FireResistance;
    [FieldOffset(182)] public readonly short MagicResistance;
    [FieldOffset(184)] public readonly short PoisonResistance;
    /// <summary>Native BonusT[8], BonusG[8], BonusP[8]. Meanings of individual bonus codes remain separate research.</summary>
    [FieldOffset(186)] public readonly SacredEquipmentBonusTypes BonusTypes;
    [FieldOffset(202)] public readonly SacredEquipmentBonusGroups BonusGroups;
    [FieldOffset(234)] public readonly SacredEquipmentBonusValues BonusValues;
    /// <summary>Native minOld[7], legacy requirement bytes; not current minimum requirements.</summary>
    [FieldOffset(250)] public readonly SacredEquipmentLegacyRequirements LegacyRequirements;
    [FieldOffset(257)] public readonly byte BlacksmithLevel;
}

// each entry is 258 bytes, with some fields at fixed offsets
// debug view with ItemId, Name, Width, Height, TypeIdentifier
[DebuggerDisplay("{IdemId}: {Name}, Class = {EffectiveCharacterClassMask}, Type = {EquipmentType}, RarityTier = {RarityTier}")]
public readonly record struct SacredEquipment(
    ItemsPakEntry Item, // legacy upper half of PreviewScale
    Vector3 PreviewRotation, // radians at offsets 4, 8, and 12
    byte Width, // offset 28
    byte Height, // offset 29
    byte UsageIdentifier, // legacy high byte of BaseItemId
    string Name, // null-terminated 64-byte field at offset 40
    uint IdemId, // 4 bytes at offset 128
    SacredEquipmentClassification Classification,
    SacredEquipmentDamage Damage
)
{
    public float PreviewScale { get; init; }
    public Vector3 PreviewOffset { get; init; }
    public uint BaseItemId { get; init; }
    public SacredEquipmentBonusTypes BonusTypes { get; init; }
    public SacredEquipmentBonusGroups BonusGroups { get; init; }
    public SacredEquipmentBonusValues BonusValues { get; init; }

    // iso 8859-1 encoding for german text
    private static readonly Encoding SacredEncoding = Encoding.GetEncoding("iso-8859-1");

    public SacredCharacterClassMask EffectiveCharacterClassMask => Classification.EffectiveCharacterClassMask;
    public SacredEquipmentType EquipmentType => Classification.EquipmentType;
    public SacredEquipmentRarityTier RarityTier => Classification.RarityTier;
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
        var item = items[checked((ushort)itemId)];
        var classification = SacredEquipmentClassification.FromBytes(
            characterClassMaskCode: (byte)layout.CharacterClassMask,
            equipmentTypeCode: (byte)layout.EquipmentType,
            rarityAndClassFlags: layout.RarityAndClassFlags
        );

        return new SacredEquipment(Item: item,
            PreviewRotation: new Vector3(
                layout.PreviewRotationX,
                layout.PreviewRotationY,
                layout.PreviewRotationZ
            ),
            Width: layout.Width,
            Height: layout.Height,
            UsageIdentifier: layout.UsageIdentifier,
            Name: name,
            IdemId: itemId,
            Classification: classification,
            Damage: layout.EquipmentDamage
        )
        {
            PreviewScale = layout.PreviewScale,
            PreviewOffset = new Vector3(
                layout.PreviewOffsetX,
                layout.PreviewOffsetY,
                layout.PreviewOffsetZ),
            BaseItemId = layout.BaseItemId,
            BonusTypes = layout.BonusTypes,
            BonusGroups = layout.BonusGroups,
            BonusValues = layout.BonusValues
        };
    }

    private static string ReadName(ReadOnlySpan<byte> bytes)
    {
        var nameBytes = bytes.Slice(SacredEquipmentLayout.NameOffset, SacredEquipmentLayout.NameLength);
        var nullIndex = nameBytes.IndexOf((byte)0);
        return SacredEncoding.GetString(nullIndex < 0 ? nameBytes : nameBytes[..nullIndex]);
    }
}
