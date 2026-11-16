using System.Buffers.Binary;
using System.Reflection.PortableExecutable;
using Sacred.Core.Executable.Layouts;

namespace Sacred.Assets.Executable;

/// <summary>Read-only PE view shared by session definition readers. Virtual
/// zero-filled data belongs to the executable's loader image, not another file.</summary>
internal sealed class SacredSessionExecutableImage : IDisposable
{
    private readonly byte[] _image;
    private readonly PEReader _pe;
    public int ImporterStart { get; }
    public bool Gold { get; }

    public SacredSessionExecutableImage(byte[] image)
    {
        _ = SacredSessionImportDefinitions.Read(image);
        _image = image; _pe = new PEReader(new MemoryStream(image, writable: false));
        ImporterStart = image.AsSpan().IndexOf(SacredExecutableSessionImportLayout.Signature) - SacredExecutableSessionImportLayout.SignatureOffset;
        Gold = image[ImporterStart + SacredExecutableSessionIntegerLayout.GoldSpeedCall] == 0xe8;
    }

    public uint FollowCall(int at)
    {
        if (at < 0 || at > _image.Length - 5 || _image[at] != 0xe8) throw new InvalidDataException("Unknown original session call.");
        foreach (var section in _pe.PEHeaders.SectionHeaders)
            if (at >= section.PointerToRawData && at - section.PointerToRawData < section.SizeOfRawData)
            {
                var address = checked((long)_pe.PEHeaders.PEHeader!.ImageBase + section.VirtualAddress + at - section.PointerToRawData);
                return checked((uint)(address + 5 + BinaryPrimitives.ReadInt32LittleEndian(_image.AsSpan(at + 1))));
            }
        throw new InvalidDataException("Original call lies outside stored sections.");
    }

    public int FindUnique(ReadOnlySpan<byte> signature)
    {
        var at = _image.AsSpan().IndexOf(signature);
        if (at < 0 || _image.AsSpan(at + signature.Length).IndexOf(signature) >= 0)
            throw new InvalidDataException("Original settings instruction sequence missing or ambiguous.");
        return at;
    }
    public uint Address(int at)
    {
        foreach (var section in _pe.PEHeaders.SectionHeaders)
            if (at >= section.PointerToRawData && at - section.PointerToRawData < section.SizeOfRawData)
                return checked((uint)(_pe.PEHeaders.PEHeader!.ImageBase + (uint)section.VirtualAddress + (uint)(at - section.PointerToRawData)));
        throw new InvalidDataException("Original instruction lies outside stored sections.");
    }
    public IReadOnlyList<int> Callers(uint address)
    {
        var result = new List<int>();
        foreach (var section in _pe.PEHeaders.SectionHeaders)
        {
            var begin = section.PointerToRawData; var end = Math.Min(_image.Length, begin + section.SizeOfRawData);
            for (var at = begin; at <= end - 5; at++)
                if (_image[at] == 0xe8 && (long)Address(at) + 5 + BinaryPrimitives.ReadInt32LittleEndian(_image.AsSpan(at + 1)) == address) result.Add(at);
        }
        return result;
    }

    public uint Word(uint address) => BinaryPrimitives.ReadUInt32LittleEndian(Bytes(address, 4));
    public uint Operand(uint getter, int offset, byte opcode)
    {
        if (Bytes(getter + (uint)offset - 1, 1)[0] != opcode) throw new InvalidDataException("Unknown original session operand.");
        return Word(getter + (uint)offset);
    }
    public byte[] Literal(uint getter, int offset, byte opcode) => Text(Operand(getter, offset, opcode));
    public byte[] Range(uint getter, int scanOperand)
    {
        var length = Text(Operand(getter, scanOperand, 0xbf)).Length;
        var endOperand = getter + (uint)(scanOperand + SacredExecutableSessionStringLayout.KeyEndBaseDistance);
        if (!Bytes(endOperand - 2, 2).SequenceEqual<byte>([0x81, 0xc1])) throw new InvalidDataException("Unknown original session range-end base.");
        var begin = Operand(getter, scanOperand + SacredExecutableSessionStringLayout.KeyRangeStartDistance, 0x68);
        var end = checked(Word(endOperand) + (uint)length);
        if (end < begin || end - begin > 4096) throw new InvalidDataException("Original string range exceeds compared bounds.");
        return Bytes(begin, (int)(end - begin)).ToArray();
    }
    public byte[] Text(uint address)
    {
        var result = new List<byte>();
        for (uint index = 0; index < 4096; index++)
        {
            var value = Bytes(checked(address + index), 1)[0];
            if (value == 0) return result.ToArray(); result.Add(value);
        }
        throw new InvalidDataException("Original session string exceeds compared bounds.");
    }
    public ReadOnlySpan<byte> Bytes(uint address, int count)
    {
        var imageBase = _pe.PEHeaders.PEHeader!.ImageBase;
        if (address < imageBase || count < 0) throw new InvalidDataException("Invalid original session address.");
        var rva = (ulong)address - imageBase;
        foreach (var section in _pe.PEHeaders.SectionHeaders)
        {
            if (rva < (uint)section.VirtualAddress) continue;
            var relative = rva - (uint)section.VirtualAddress;
            var extent = (ulong)Math.Max(section.SizeOfRawData, section.VirtualSize);
            if (relative >= extent || (ulong)count > extent - relative) continue;
            if (relative >= (uint)section.SizeOfRawData) return new byte[count];
            var stored = Math.Min(count, section.SizeOfRawData - (int)relative);
            var at = checked(section.PointerToRawData + (int)relative);
            if (at < 0 || at > _image.Length - stored) break;
            if (stored == count) return _image.AsSpan(at, count);
            var bytes = new byte[count]; _image.AsSpan(at, stored).CopyTo(bytes); return bytes;
        }
        throw new InvalidDataException("Original session operand exceeds stored/zero-filled section bounds.");
    }
    public void Dispose() => _pe.Dispose();
}
