using System.Reflection.PortableExecutable;

namespace Sacred.Network.Admission;

/// <summary>Bounded reads of the original PE32 data used by Gold's network build calculation.</summary>
internal sealed class GoldPeImage
{
    private readonly byte[] _image;
    private readonly PEHeaders _headers;

    public ulong ImageBase { get; }

    public GoldPeImage(byte[] image)
    {
        _image = image;
        using var reader = new PEReader(new MemoryStream(image, writable: false));
        _headers = reader.PEHeaders;
        var pe = _headers.PEHeader ?? throw new InvalidDataException("Missing PE header.");
        if (_headers.CoffHeader.Machine != Machine.I386 || pe.Magic != PEMagic.PE32)
            throw new InvalidDataException("Gold network build metadata requires an x86 PE32 image.");
        ImageBase = pe.ImageBase;
    }

    public ReadOnlySpan<byte> At(uint rva, int length)
    {
        foreach (var section in _headers.SectionHeaders)
        {
            if (rva < (uint)section.VirtualAddress ||
                (ulong)rva + (uint)length > (ulong)section.VirtualAddress + (uint)section.SizeOfRawData) continue;
            var offset = (long)section.PointerToRawData + rva - section.VirtualAddress;
            if (offset < 0 || offset > _image.Length - length) break;
            return _image.AsSpan((int)offset, length);
        }
        throw new InvalidDataException("Gold network build metadata is outside mapped executable data.");
    }
}
