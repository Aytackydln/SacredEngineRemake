using System.Collections.Frozen;
using System.Diagnostics;
using System.Text;
using Sacred.Core.Items;
using SacredItemSimulator.GamePak;
using SacredItemSimulator.Utils;

namespace Sacred.Core.Weapon;

// each entry is 258 bytes, with some fields at fixed offsets
// debug view with ItemId, Name, Width, Height, TypeIdentifier
[DebuggerDisplay("{IdemId}: {Name}, Type = {TypeIdentifier}")]
public readonly record struct SacredEquipment(
    SacredPakLocation PakLocation, // location of the entry in the pak file, useful for debugging and lookup
    ItemsPakEntry Item,
    ushort Short1, // 2 bytes at offset 0
    ushort Short2, // 2 bytes at offset 8
    byte[] SpanX, // 20 bytes at offset 10-12
    byte Width, // 1 byte at offset 26
    byte Height, // 1 byte at offset 27
    byte TypeIdentifier, // 1 byte at offset 37
    string Name, // 88 bytes at offset 38-125, null-terminated string in iso 8859-1 encoding
    uint IdemId, // 4 bytes at offset 126
    byte[] UnknownBytes // 64 bytes at offset 194-257
)
{
    private const int Size = 258;

    // iso 8859-1 encoding for german text
    private static readonly Encoding SacredEncoding = Encoding.GetEncoding("iso-8859-1");

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
        var itemId = BitConverter.ToUInt16(bytes[126..128]);
        var item = items[itemId];

        var span1 = bytes[2..8];
        var span2 = bytes[12..32];
        var span3 = bytes[28..37];
        var span4 = bytes[130..194];
        var span5 = bytes[194..258];

        var unknownBytes = ByteArrayUtils.Combine(span1, span2, span3, span4, span5);

        return new SacredEquipment(
            PakLocation: pakLocation,
            Item: item,
            Short1: BitConverter.ToUInt16(bytes[..2]),
            Short2: BitConverter.ToUInt16(bytes[8..10]),
            SpanX: bytes[10..12].ToArray(),
            Width: width,
            Height: height,
            TypeIdentifier: bytes[37],
            Name: name,
            IdemId: itemId,
            UnknownBytes: unknownBytes
        );
    }
}