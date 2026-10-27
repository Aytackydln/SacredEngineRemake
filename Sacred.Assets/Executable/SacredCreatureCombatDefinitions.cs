using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Sacred.Core.Runtime;

namespace Sacred.Assets.Executable;

/// <summary>Original profile table operands used by Gold 0x57B1D3. The index
/// remains the native runtime byte; it is not an item or creature resource ID.</summary>
public sealed class SacredCreatureCombatDefinitions
{
    private readonly SacredNativeTypeSwitch _image;
    private readonly uint _table;
    private SacredCreatureCombatDefinitions(SacredNativeTypeSwitch image, uint table) => (_image, _table) = (image, table);
    public SacredCreatureCombatProfileLayout Profile(byte index)
        => MemoryMarshal.Read<SacredCreatureCombatProfileLayout>(_image.At(_table + index * (uint)SacredCreatureCombatProfileLayout.Size, SacredCreatureCombatProfileLayout.Size));

    public static SacredCreatureCombatDefinitions Read(byte[] image)
    {
        ReadOnlySpan<byte> prefix = [0xC1, 0xE0, 6, 0xD9, 0xC1, 0xD8, 0x88];
        var found = -1;
        for (var search = 0; search <= image.Length - 60;)
        {
            var relative = image.AsSpan(search).IndexOf(prefix);
            if (relative < 0) break;
            var at = search + relative;
            search = at + prefix.Length;
            if (at > image.Length - 60 || !image.AsSpan(at + 11, 4).SequenceEqual<byte>([0xD9, 0xC1, 0xD8, 0x88]) ||
                !image.AsSpan(at + 15, 4).SequenceEqual(BitConverter.GetBytes(BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(at + 7)) - 4))) continue;
            if (found >= 0) throw new InvalidDataException("Ambiguous original creature result profiles.");
            found = at;
        }
        if (found < 0) throw new InvalidDataException("Original creature result profiles unavailable.");
        var reader = new SacredNativeTypeSwitch(image);
        var table = BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(found + 15));
        _ = reader.At(table, SacredCreatureCombatProfileLayout.Size);
        return new(reader, table);
    }
}
