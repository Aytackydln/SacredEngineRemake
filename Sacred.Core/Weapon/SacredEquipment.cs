using System.Collections.Frozen;
using System.Diagnostics;
using System.Numerics;
using System.Text;
using Sacred.Core.Items;
using SacredItemSimulator.GamePak;
using SacredItemSimulator.Utils;

namespace Sacred.Core.Weapon;

// each entry is 258 bytes, with some fields at fixed offsets
// debug view with ItemId, Name, Width, Height, TypeIdentifier
[DebuggerDisplay("{IdemId}: {Name}, Class = {EffectiveCharacterClassMask}, Type = {EquipmentType}, Unique = {IsUnique}")]
public readonly record struct SacredEquipment(
    SacredPakLocation PakLocation, // location of the entry in the pak file, useful for debugging and lookup
    ItemsPakEntry Item,
    ushort Short1, // 2 bytes at offset 0
    Vector3 PreviewRotation, // candidate item preview rotation: three unaligned floats at offsets 2, 6, and 10 in radians
    ushort Short2, // legacy overlapping interpretation of bytes at offset 8
    byte Width, // 1 byte at offset 26
    byte Height, // 1 byte at offset 27
    byte UsageIdentifier, // 1 byte at offset 28; weapon/animation shape, partly tied to handedness
    byte TypeIdentifier, // 1 byte at offset 37; observed as 0 in sampled equipment
    string Name, // 88 bytes at offset 38-125, null-terminated string in iso 8859-1 encoding
    uint IdemId, // 2 bytes at offset 126, kept as uint for existing dictionary keys
    SacredEquipmentClassification Classification
)
{
    private const int Size = 258;

    // iso 8859-1 encoding for german text
    private static readonly Encoding SacredEncoding = Encoding.GetEncoding("iso-8859-1");

    public byte CharacterClassMaskCode => Classification.CharacterClassMaskCode;
    public SacredCharacterClassMask CharacterClassMask => Classification.CharacterClassMask;
    public SacredCharacterClassMask EffectiveCharacterClassMask => Classification.EffectiveCharacterClassMask;
    public byte EquipmentTypeCode => Classification.EquipmentTypeCode;
    public SacredEquipmentType EquipmentType => Classification.EquipmentType;
    public byte RarityAndClassFlags => Classification.RarityAndClassFlags;
    public byte RarityTierCode => Classification.RarityTierCode;
    public byte ClassFlagCode => Classification.ClassFlagCode;
    public bool IsUnique => Classification.IsUnique;
    public SacredEquipmentLore InferredLore => Classification.InferLore(Short2);
    public SacredEquipmentSlot InferredSlot => Classification.InferSlot();
    public SacredEquipmentHandedness InferredHandedness => Classification.InferHandedness(UsageIdentifier, Short2);

    public bool? InferredTwoHanded => InferredHandedness switch
    {
        SacredEquipmentHandedness.TwoHanded => true,
        SacredEquipmentHandedness.OneHanded or SacredEquipmentHandedness.NotApplicable => false,
        _ => null
    };

    public static SacredEquipment FromBytes(
        BinaryReader br,
        SacredPakFile sacredFile,
        FrozenDictionary<ushort, ItemsPakEntry> items
    )
    {
        var offset = br.BaseStream.Position;
        var pakLocation = new SacredPakLocation(sacredFile, offset, 258);

        // marshall to WeaponPackEntry struct
        var bytes = br.ReadBytes(Size).AsSpan();

        var nameBytes = bytes[38..126];
        var nullIndex = nameBytes.IndexOf((byte)0);

        var name = SacredEncoding.GetString(nameBytes[..nullIndex]);

        // TODO figure out
        //if (GameResStore.ReverseIndexMap.TryGetValue(name, out var resId))
        //{
        //    name = GameResStore.Strings.GetValueOrDefault(resId, name);
        //}

        var width = bytes[26];
        var height = bytes[27];
        var usageIdentifier = bytes[28];
        var itemId = BitConverter.ToUInt16(bytes[126..128]);
        var item = items[itemId];
        var classification = SacredEquipmentClassification.FromBytes(
            characterClassMaskCode: bytes[130],
            equipmentTypeCode: bytes[131],
            rarityAndClassFlags: bytes[132]
        );

        return new SacredEquipment(
            PakLocation: pakLocation,
            Item: item,
            Short1: BitConverter.ToUInt16(bytes[..2]),
            PreviewRotation: new Vector3(
                BitConverter.ToSingle(bytes[2..6]),
                BitConverter.ToSingle(bytes[6..10]),
                BitConverter.ToSingle(bytes[10..14])    //This is absolutely correct
            ),
            Short2: BitConverter.ToUInt16(bytes[8..10]),
            Width: width,
            Height: height,
            UsageIdentifier: usageIdentifier,
            TypeIdentifier: bytes[37],
            Name: name,
            IdemId: itemId,
            Classification: classification
        );
    }
}