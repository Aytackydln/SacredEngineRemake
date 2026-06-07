using System.Runtime.InteropServices;
using SacredItemSimulator.GamePak;

namespace Sacred.Core.Items;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly record struct ItemsPakEntryInfo(
    SacredPakLocation PakLocation, // location of the entry in the pak file, useful for debugging and lookup
    ushort ItemIndex,

    uint ModelDescOffset, // 4 bytes at offset 14
    // rest is unknown, but we can read it as raw bytes for now
    bool Byte23 // 1 byte at offset 23

    // rest of the bytes are always same, no need to parse
)
{
    private const int Length = 12;

    public static ItemsPakEntryInfo FromBytes(ushort entryIndex, SacredPakFile pakFile, BinaryReader br)
    {
        var pakLocation = new SacredPakLocation(pakFile, br.BaseStream.Position, Length);
        var bytes = br.ReadBytes(Length).AsSpan();

        var modelDescOffset = BitConverter.ToUInt32(bytes[2..6]);

        return new ItemsPakEntryInfo(
            ItemIndex: entryIndex,
            PakLocation: pakLocation,
            ModelDescOffset: modelDescOffset,
            Byte23: bytes[10] == 0x02 // 0x00 or 0x02, treat as bool for now
        );
    }
}