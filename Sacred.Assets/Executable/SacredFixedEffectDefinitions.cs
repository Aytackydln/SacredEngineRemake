using System.Buffers.Binary;

namespace Sacred.Assets.Executable;

/// <summary>Compared fixed-portal initialization operands from original Gold
/// code. Family/resource selection follows the executable; unsupported families
/// have no fabricated recipe.</summary>
public sealed record SacredFixedEffectDefinitions(uint Resource, uint EventCode, uint AngleBits,
    uint SelectionMask, uint SelectionMaximum, uint ParticleCapacity, uint NodePeriod,
    uint BaseFlags, uint RechargeBits, uint WordE0Bits)
{
    public static SacredFixedEffectDefinitions Load(string directory)
    {
        Exception? failure = null;
        foreach (var name in new[] { "Sacred.exe", "pureHD.exe" })
        {
            var path = Path.Combine(directory, name);
            if (!File.Exists(path)) continue;
            try { return Read(File.ReadAllBytes(path)); }
            catch (InvalidDataException error) { failure = error; }
        }
        throw new InvalidDataException("Original fixed Effect initialization unavailable.", failure);
    }

    public static SacredFixedEffectDefinitions Read(byte[] image)
    {
        var code = new SacredNativeTypeSwitch(image);
        ReadOnlySpan<byte> startPattern = [0x56, 0x57, 0x8B, 0xF1, 0x33, 0xFF, 0x89, 0xBE, 0xA4, 0x20, 0, 0,
            0x89, 0xBE, 0xA0, 0x20, 0, 0, 0xC6, 0x86, 0xC0, 0x20, 0, 0, 1];
        SacredFixedEffectDefinitions? result = null;
        Span<byte> store = stackalloc byte[6]; store[0] = 0xC7; store[1] = 0x03;
        for (var search = 0; search < image.Length;)
        {
            var found = image.AsSpan(search).IndexOf(startPattern);
            if (found < 0) break;
            var start = search + found; search = start + 1;
            var startBody = image.AsSpan(start, 0x75);
            if (startBody[0x1E] != 0x25 || startBody[0x4D] != 0xB8 ||
                !startBody[0x65..].StartsWith<byte>([0xC7, 0x86, 0xCC, 0x20, 0, 0]))
                throw new InvalidDataException("Unsupported fixed particle Start operands.");
            var pointer = new byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(pointer, checked((uint)code.Address(start)));
            var vtableOffset = Unique(image, pointer) - 16;
            var vtable = checked((uint)code.Address(vtableOffset));
            BinaryPrimitives.WriteUInt32LittleEndian(store[2..], vtable);
            var assignment = Unique(image, store);
            var constructor = assignment - 0xA7;
            if (!image.AsSpan(constructor + 0x26).StartsWith<byte>([0x50, 0x51, 0x68]) ||
                !image.AsSpan(constructor).StartsWith<byte>([0x6A, 0xFF, 0x68]))
                throw new InvalidDataException("Unsupported fixed particle constructor header.");
            var resource = Word(image.AsSpan(constructor + 0x29));
            // Gold also has a matching particle implementation outside this
            // CreateObj event dispatch. Select by the original caller's table.
            var eventCode = Event(image, code, resource);
            if (eventCode is null) continue;
            var capacity = image.AsSpan(constructor, 0x700).IndexOf((ReadOnlySpan<byte>)[0xC7, 0x83, 0x9C, 0, 0, 0]);
            if (capacity < 0) throw new InvalidDataException("Particle capacity operand unavailable.");
            var fx = Unique(image, [0x66, 0x89, 0x5E, 0x48, 0x66, 0x89, 0x5E, 0x6E, 0x66, 0x89, 0x5E, 0x70]);
            var fxConstructor = fx - 0x67;
            if (!image.AsSpan(fxConstructor + 0x1E).StartsWith<byte>([0x55, 0x6A]) ||
                !image.AsSpan(fxConstructor + 0x116).StartsWith<byte>([0xC7, 0x86, 0xDC, 0, 0, 0]) ||
                !image.AsSpan(fxConstructor + 0x120).StartsWith<byte>([0xC7, 0x86, 0xE0, 0, 0, 0]))
                throw new InvalidDataException("Unsupported cObjectFX initialization operands.");
            var candidate = new SacredFixedEffectDefinitions(resource, eventCode.Value, Word(startBody[0x4E..]),
                Word(startBody[0x1F..]) & 0x7FFFFFFF, Word(startBody[0x6B..]),
                Word(image.AsSpan(constructor + capacity + 6)), image[fxConstructor + 0x20],
                Flags(image, code, resource), Word(image.AsSpan(fxConstructor + 0x11C)), Word(image.AsSpan(fxConstructor + 0x126)));
            if (result is not null) throw new InvalidDataException("Ambiguous fixed Effect family in original CreateObj dispatch.");
            result = candidate;
        }
        return result ?? throw new InvalidDataException("Original fixed Effect family is unavailable.");
    }

    internal static uint? Event(byte[] image, SacredNativeTypeSwitch code, uint resource)
    {
        var at = Unique(image, [0x0F, 0x87, 0x4C, 0x17, 0, 0, 0x33, 0xC9, 0x8A, 0x88]) - 31;
        var body = image.AsSpan(at, 52);
        if (!body.StartsWith<byte>([0x8B, 0x46, 0x10, 0x83, 0xC4, 8, 0x3D]) ||
            body[23] != 0x05 || body[28] != 0x83 || body[39] != 0x8A || body[45] != 0xFF)
            throw new InvalidDataException("Unsupported CreateObj FX dispatch.");
        var index = unchecked(resource + Word(body[24..]));
        if (index > body[30]) return null;
        var selector = code.At(Word(body[41..]) + index, 1)[0];
        var target = Word(code.At(Word(body[48..]) + (uint)selector * 4, 4));
        var selected = code.At(target, 0x70);
        var id = selected.IndexOf((ReadOnlySpan<byte>)[0x89, 0x51, 4, 0x8B, 0x50, 8]);
        if (id < 2 || selected[id - 2] != 0x6A)
            throw new InvalidDataException("Fixed Effect event operand unavailable.");
        return selected[id - 1];
    }

    internal static uint Flags(byte[] image, SacredNativeTypeSwitch code, uint resource)
    {
        var at = Unique(image, [0x8B, 0x56, 0x14, 0x8B, 0x46, 0x10, 0x80, 0x4E, 0x48, 1, 0x81, 0xCA]);
        var body = image.AsSpan(at, 0x100);
        var first = body.IndexOf((ReadOnlySpan<byte>)[0x8D, 0x88]);
        if (first < 0 || body[first + 6] != 0x83 || body[first + 13] != 0x8A)
            throw new InvalidDataException("Unsupported fixed Effect ownership predicate.");
        var index = unchecked(resource + Word(body[(first + 2)..]));
        if (index > body[first + 8]) throw new InvalidDataException("Fixed Effect ownership range is uncompared.");
        var selector = code.At(Word(body[(first + 15)..]) + index, 1)[0];
        var target = Word(code.At(Word(body[(first + 22)..]) + (uint)selector * 4, 4));
        var selected = code.At(target, 9);
        if (!selected.StartsWith<byte>([0x81, 0xE7])) throw new InvalidDataException("Fixed Effect ownership clearing is uncompared.");
        var flags = (2u | Word(body[12..])) & Word(selected[2..]);
        // The final predicate can add the original base flag 4.
        var last = body.IndexOf((ReadOnlySpan<byte>)[0x05, 0xFD, 0xFC, 0xFF, 0xFF, 0x3D]);
        if (last < 0) throw new InvalidDataException("Fixed Effect final predicate unavailable.");
        index = unchecked(resource + Word(body[(last + 1)..]));
        if (index <= Word(body[(last + 6)..]))
        {
            selector = code.At(Word(body[(last + 16)..]) + index, 1)[0];
            target = Word(code.At(Word(body[(last + 23)..]) + (uint)selector * 4, 4));
            selected = code.At(target, 9);
            if (selected.StartsWith<byte>([0x8B, 0x46, 0x14, 0x0C])) flags |= selected[4];
            else if (!selected.StartsWith<byte>([0xB0, 1])) throw new InvalidDataException("Unknown fixed Effect final flag action.");
        }
        return flags;
    }
    private static int Unique(byte[] image, ReadOnlySpan<byte> pattern)
    {
        var at = image.AsSpan().IndexOf(pattern);
        if (at < 0 || image.AsSpan(at + 1).IndexOf(pattern) >= 0) throw new InvalidDataException("Unique original Effect code anchor unavailable.");
        return at;
    }
    private static uint Word(ReadOnlySpan<byte> bytes) => BinaryPrimitives.ReadUInt32LittleEndian(bytes);
}
