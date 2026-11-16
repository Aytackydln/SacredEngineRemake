using System.Buffers.Binary;
using System.Reflection.PortableExecutable;
using Sacred.Core.Executable;
using Sacred.Core.Executable.Layouts;

namespace Sacred.Assets.Executable;

/// <summary>Follow the original controller+0x40 assignment's relative call to
/// its getter, keys/defaults and numeric parser's default CRT table. No copied
/// setting names, port defaults or .NET character classification are supplied.</summary>
public static class SacredListenPortDefinitions
{
    public static SacredListenPortDefinition Read(byte[] image)
    {
        ArgumentNullException.ThrowIfNull(image);
        using var pe = new PEReader(new MemoryStream(image, writable: false));
        var headers = pe.PEHeaders;
        var imageBase = headers.PEHeader?.ImageBase ?? throw new InvalidDataException("Missing executable image base.");
        var candidates = new List<int>();
        for (var position = 0; position <= image.Length - 4;)
        {
            var next = image.AsSpan(position).IndexOf(SacredExecutableListenPortLayout.DestinationWrite);
            if (next < 0) break;
            var at = position + next;
            if (at > image.Length - 4) break;
            if (at >= SacredExecutableListenPortLayout.GoldCallDistance && image[at - SacredExecutableListenPortLayout.GoldCallDistance] == 0xe8 && image[at + 3] == 0x50)
                candidates.Add(at - SacredExecutableListenPortLayout.GoldCallDistance);
            else if (at >= SacredExecutableListenPortLayout.PureHdCallDistance && image[at - SacredExecutableListenPortLayout.PureHdCallDistance] == 0xe8 && image[at - 6] == 0x50 && image[at - 5] == 0x68 && image[at + 3] == 0x68)
                candidates.Add(at - SacredExecutableListenPortLayout.PureHdCallDistance);
            position = at + 1;
        }
        if (candidates.Count != 1) throw new InvalidDataException("Original listen-port caller missing or ambiguous.");
        var getter = RelativeCall(candidates[0]);
        var body = At(getter, SacredExecutableListenPortLayout.GetterBodySize).ToArray();
        if (!body.AsSpan(0, 3).SequenceEqual<byte>([0x6a, 0xff, 0x68]) || body[^1] != 0xc3)
            throw new InvalidDataException("Unknown original listen-port getter extent.");
        var thunk = RelativeCall(FileOffset(getter) + SacredExecutableListenPortLayout.NumericCallOffset);
        var parser = RelativeCall(FileOffset(thunk) + SacredExecutableListenPortLayout.NumericThunkCallOffset);
        var classPointer = BinaryPrimitives.ReadUInt32LittleEndian(At(parser + SacredExecutableListenPortLayout.ClassPointerOperand, 4));
        var classAddress = BinaryPrimitives.ReadUInt32LittleEndian(At(classPointer, 4));
        var classes = new ushort[SacredExecutableListenPortLayout.ClassCount];
        var classBytes = At(classAddress, classes.Length * 2);
        for (var index = 0; index < classes.Length; index++) classes[index] = BinaryPrimitives.ReadUInt16LittleEndian(classBytes[(index * 2)..]);
        return new(Key(SacredExecutableListenPortLayout.ReadKeyOperand), Key(SacredExecutableListenPortLayout.DefaultKeyOperand),
            Immediate(SacredExecutableListenPortLayout.DefaultWriteOperand, 0x68), Immediate(SacredExecutableListenPortLayout.DefaultReturnOperand, 0xb8), classes);

        uint Immediate(int offset, byte opcode)
        {
            if (body[offset - 1] != opcode) throw new InvalidDataException("Unknown listen-port immediate operand.");
            return BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(offset));
        }
        byte[] Key(int offset)
        {
            var address = Immediate(offset, 0xbf); uint length = 0;
            for (uint index = 0; index < 4096; index++)
            {
                var character = At(checked(address + index), 1)[0];
                if (character != 0) continue;
                length = index; break;
            }
            if (At(checked(address + length), 1)[0] != 0) throw new InvalidDataException("Original listen-port key exceeds compared bounds.");
            var endOperand = offset + SacredExecutableListenPortLayout.KeyEndBaseOperandDistance;
            if (!body.AsSpan(endOperand - 2, 2).SequenceEqual<byte>([0x81, 0xc1]))
                throw new InvalidDataException("Unknown original listen-port key range-end instruction.");
            var begin = Immediate(offset + SacredExecutableListenPortLayout.KeyRangeStartOperandDistance, 0x68);
            var end = checked(BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(endOperand)) + length);
            if (end < begin || end - begin > 4096) throw new InvalidDataException("Original listen-port key range exceeds compared bounds.");
            return At(begin, (int)(end - begin)).ToArray();
        }
        uint RelativeCall(int at)
        {
            if (at < 0 || at > image.Length - 5 || image[at] != 0xe8) throw new InvalidDataException("Unknown original numeric/getter call.");
            var relative = BinaryPrimitives.ReadInt32LittleEndian(image.AsSpan(at + 1));
            return checked((uint)((long)Address(at) + 5 + relative));
        }
        uint Address(int at)
        {
            foreach (var section in headers.SectionHeaders)
                if (at >= section.PointerToRawData && at - section.PointerToRawData < section.SizeOfRawData)
                    return checked((uint)(imageBase + (uint)section.VirtualAddress + (uint)(at - section.PointerToRawData)));
            throw new InvalidDataException("Call lies outside original stored sections.");
        }
        int FileOffset(uint address)
        {
            if (address < imageBase) throw new InvalidDataException("Address precedes original image.");
            var rva = (ulong)address - imageBase;
            foreach (var section in headers.SectionHeaders)
            {
                if (rva < (uint)section.VirtualAddress) continue;
                var relative = rva - (uint)section.VirtualAddress;
                if (relative < (uint)section.SizeOfRawData) return checked(section.PointerToRawData + (int)relative);
            }
            throw new InvalidDataException("Operand lies outside original stored sections.");
        }
        ReadOnlySpan<byte> At(uint address, int count)
        {
            var at = FileOffset(address);
            if (at < 0 || at > image.Length - count) throw new InvalidDataException("Truncated original operand/data.");
            return image.AsSpan(at, count);
        }
    }

    public static SacredListenPortDefinition Load(string directory)
    {
        Exception? failure = null;
        foreach (var name in new[] { "Sacred.exe", "pureHD.exe" })
        {
            var path = Path.Combine(directory, name); if (!File.Exists(path)) continue;
            try { return Read(File.ReadAllBytes(path)); } catch (InvalidDataException error) { failure = error; }
        }
        throw new InvalidDataException("Original listen-port definitions unavailable.", failure);
    }
}
