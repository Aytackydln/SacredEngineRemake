using System.Buffers.Binary;
using System.Reflection.PortableExecutable;

namespace Sacred.Assets.Executable;

/// <summary>Original raw KEYX region to script-region lookup. The native getter indexes
/// a signed-byte table; callback numbers are zero-extended from its signed 16-bit result.</summary>
public sealed class SacredRegionDefinitions
{
    private readonly byte[] _mapping;
    private SacredRegionDefinitions(byte[] mapping) => _mapping = mapping;
    public ushort ScriptRegion(byte rawRegion) => unchecked((ushort)(short)(sbyte)_mapping[rawRegion]);

    public static SacredRegionDefinitions Load(string directory)
    {
        Exception? failure = null;
        foreach (var filename in new[] { "Sacred.exe", "pureHD.exe" })
        {
            var path = Path.Combine(directory, filename);
            if (!File.Exists(path)) continue;
            try { return Read(File.ReadAllBytes(path)); }
            catch (InvalidDataException error) { failure = error; }
        }
        throw new InvalidDataException("Original script-region mapping unavailable.", failure);
    }

    public static SacredRegionDefinitions Read(byte[] image)
    {
        using var pe = new PEReader(new MemoryStream(image, writable: false));
        var headers = pe.PEHeaders;
        var imageBase = headers.PEHeader?.ImageBase ?? throw new InvalidDataException("Missing PE header.");
        ReadOnlySpan<byte> getter = [0x8B, 0x44, 0x24, 4, 0x25, 0xFF, 0xFF, 0, 0, 0x66, 0x0F, 0xBE, 0x80];
        for (var offset = 0; offset < image.Length - getter.Length - 5; offset++)
        {
            if (!image.AsSpan(offset, getter.Length).SequenceEqual(getter) || image[offset + getter.Length + 4] != 0xC3) continue;
            var address = BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(offset + getter.Length));
            if (address < imageBase) continue;
            var rva = address - imageBase;
            foreach (var section in headers.SectionHeaders)
                if (rva >= (ulong)section.VirtualAddress && rva + 256 <= (ulong)section.VirtualAddress + (ulong)section.SizeOfRawData)
                    return new(image.AsSpan(checked(section.PointerToRawData + (int)(rva - (ulong)section.VirtualAddress)), 256).ToArray());
        }
        throw new InvalidDataException("Original signed-byte script-region getter not found.");
    }
}
