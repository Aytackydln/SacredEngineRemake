using System.Buffers.Binary;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Sacred.Particles.Reader.Executable;

internal sealed class SacredExecutableImage
{
    private readonly byte[] _bytes;
    public string ExecutableSha256 { get; }
    public string CodeSha256 { get; }
    public uint EndAddress => checked(SacredGoldExecutableProfile.ImageBase + (uint)_bytes.Length);

    private SacredExecutableImage(byte[] bytes, string executableHash)
    {
        _bytes = bytes;
        ExecutableSha256 = executableHash;
        CodeSha256 = Convert.ToHexStringLower(SHA256.HashData(
            Slice(SacredGoldExecutableProfile.CodeHashAddress, SacredGoldExecutableProfile.CodeHashLength)));
    }

    public static SacredExecutableImage Load(string path)
    {
        var file = File.ReadAllBytes(path);
        using var stream = new MemoryStream(file, writable: false);
        using var pe = new PEReader(stream);
        var header = pe.PEHeaders.PEHeader ?? throw new InvalidDataException("Sacred.exe has no PE optional header.");
        if (header.Magic != PEMagic.PE32 || header.ImageBase != SacredGoldExecutableProfile.ImageBase ||
            header.SizeOfImage < 0x490000 || header.SizeOfImage > 64 * 1024 * 1024)
            throw new NotSupportedException("Expected the supported 32-bit Sacred Gold executable.");
        var memory = new byte[header.SizeOfImage];
        foreach (var section in pe.PEHeaders.SectionHeaders)
        {
            var source = section.PointerToRawData;
            var target = section.VirtualAddress;
            var length = section.SizeOfRawData;
            if (source < 0 || target < 0 || length < 0 || source > file.Length - length || target > memory.Length - length)
                throw new InvalidDataException($"Invalid PE section bounds: {section.Name}.");
            file.AsSpan(source, length).CopyTo(memory.AsSpan(target, length));
        }
        var hash = Convert.ToHexStringLower(SHA256.HashData(file));
        var image = new SacredExecutableImage(memory, hash);
        if (image.CodeSha256 == SacredGoldExecutableProfile.CodeSha256) return image;
        if ((uint)header.AddressOfEntryPoint + header.ImageBase != SacredGoldExecutableProfile.LoaderEntryPoint)
            throw new NotSupportedException($"Unsupported Sacred.exe code hash {image.CodeSha256}.");

        var decodedHeader = image.Slice(SacredGoldExecutableProfile.LoaderHeaderAddress,
            SacredExecutableCodeHeaderLayout.SerializedSize).ToArray();
        DecodeWords(decodedHeader.AsSpan(4), BinaryPrimitives.ReadUInt32LittleEndian(decodedHeader));
        var codeHeader = MemoryMarshal.Read<SacredExecutableCodeHeaderLayout>(decodedHeader);
        var codeSection = pe.PEHeaders.SectionHeaders.Single(s => s.Name == ".text");
        if (codeHeader.CodeAddress != header.ImageBase + (uint)codeSection.VirtualAddress ||
            codeHeader.CodeByteLength != codeSection.SizeOfRawData)
            throw new InvalidDataException("The loader's code range differs from the PE .text section.");
        DecodeWords(memory.AsSpan(codeSection.VirtualAddress, codeSection.SizeOfRawData), codeHeader.CodeXorSeed);
        image = new SacredExecutableImage(memory, hash);
        if (image.CodeSha256 != SacredGoldExecutableProfile.CodeSha256)
            throw new NotSupportedException($"Unsupported decoded Sacred.exe code hash {image.CodeSha256}.");
        return image;
    }

    private static void DecodeWords(Span<byte> bytes, uint key)
    {
        if (bytes.Length % 4 != 0) throw new InvalidDataException("Encoded word region is not four-byte aligned.");
        for (var offset = 0; offset < bytes.Length; offset += 4)
        {
            var encoded = BinaryPrimitives.ReadUInt32LittleEndian(bytes[offset..]);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes[offset..], encoded ^ key);
            key = encoded;
        }
    }

    public ReadOnlySpan<byte> Slice(uint address, int size)
    {
        var offset = (long)address - SacredGoldExecutableProfile.ImageBase;
        if (offset < 0 || size < 0 || offset > _bytes.Length - size)
            throw new InvalidDataException($"Native address 0x{address:X8} ({size} bytes) is outside the image.");
        return _bytes.AsSpan((int)offset, size);
    }

    public byte Byte(uint address) => Slice(address, 1)[0];
    public uint UInt32(uint address) => BinaryPrimitives.ReadUInt32LittleEndian(Slice(address, 4));
    public float Single(uint address) => BinaryPrimitives.ReadSingleLittleEndian(Slice(address, 4));
    public string String(uint address, int maximumLength)
    {
        var bytes = Slice(address, maximumLength);
        var end = bytes.IndexOf((byte)0);
        if (end < 0) throw new InvalidDataException($"Unterminated native string at 0x{address:X8}.");
        return Encoding.Latin1.GetString(bytes[..end]);
    }
}
