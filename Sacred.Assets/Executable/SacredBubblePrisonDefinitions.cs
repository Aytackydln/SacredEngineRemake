using System.Buffers.Binary;
using Sacred.Core.Runtime;

namespace Sacred.Assets.Executable;

/// <summary>Original BUBBLEPRISON resource and Start operands, resolved through
/// its particle vtable and constructor. No resource identity is hardcoded.</summary>
public sealed record SacredBubblePrisonDefinitions(SacredObjectFxInitialDefinition Fx, SacredBubblePrisonParameters Parameters)
{
    public uint Resource => Fx.Resource;
    public static SacredBubblePrisonDefinitions Load(string directory)
    {
        Exception? failure = null;
        foreach (var name in new[] { "Sacred.exe", "pureHD.exe" })
        {
            var path = Path.Combine(directory, name);
            if (!File.Exists(path)) continue;
            try { return Read(File.ReadAllBytes(path)); }
            catch (InvalidDataException error) { failure = error; }
        }
        throw new InvalidDataException("Original BUBBLEPRISON initialization unavailable.", failure);
    }

    public static SacredBubblePrisonDefinitions Read(byte[] image)
    {
        var code = new SacredNativeTypeSwitch(image);
        var start = Unique(image, [0x83, 0xEC, 8, 0x53, 0x56, 0x57, 0x8B, 0xF9, 0x33, 0xDB,
            0xC7, 0x87, 0xA0, 0x20, 0, 0, 0, 0, 0, 0, 0xC6, 0x87, 0xA4, 0x20, 0, 0, 1]);
        var pointer = new byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(pointer, checked((uint)code.Address(start)));
        var table = Unique(image, pointer) - 16;
        var assignment = new byte[6]; assignment[0] = 0xC7; assignment[1] = 6;
        BinaryPrimitives.WriteUInt32LittleEndian(assignment.AsSpan(2), checked((uint)code.Address(table)));
        var constructor = Unique(image, assignment) - 0x3C;
        if (!image.AsSpan(constructor + 0x24).StartsWith<byte>([0x68]) ||
            !image.AsSpan(constructor + 0x29).StartsWith<byte>([0x8B, 0xCE, 0x89, 0x74, 0x24, 0x14, 0xE8]))
            throw new InvalidDataException("Unsupported BUBBLEPRISON constructor/resource linkage.");
        Expect(0x1F, [0x8D, 0xB7, 0xD0, 0x20, 0, 0]);
        Expect(0x3E, [0xD9, 0x5E, 0xD8, 0xE8]);
        Expect(0x60, [0xD9, 0x1E, 0xE8]);
        Expect(0x6B, [0x43, 0xDB, 0x44, 0x24, 0x10, 0x83, 0xC6, 4, 0x83, 0xFB, 0x0A]);
        Expect(0x8A, [0xDE, 0xC9, 0xD9, 0x5E, 0x24, 0x7C]);
        Expect(0xAA, [0xD9, 0x9F, 0xCC, 0x20, 0, 0, 0xE8]);
        Expect(0xB9, [0xC7, 0x87, 0x1C, 0x21, 0, 0]);
        Expect(0xC3, [0xDB, 0x44, 0x24, 0x10, 0xC7, 0x87, 0x98, 0, 0, 0, 0, 0, 0, 0,
            0xC7, 0x47, 0x7C, 0, 0, 0, 0]);
        Expect(0xEA, [0xD9, 0x9F, 0xF4, 0x20, 0, 0]);
        var scale = Operand(0x32, 0xD8, 0x0D);
        foreach (var at in new[] { 0x4E, 0x76, 0x9E, 0xD8 })
            if (Operand(at, 0xD8, 0x0D) != scale) throw new InvalidDataException("Inconsistent BUBBLEPRISON random scale.");
        var turn = Operand(0x38, 0xD8, 0x0D);
        if (Operand(0xA4, 0xD8, 0x0D) != turn) throw new InvalidDataException("Inconsistent BUBBLEPRISON angle scale.");
        return new(SacredObjectFxInitialDefinition.Read(image, Word(image.AsSpan(constructor + 0x25))),
            new(scale, turn, Operand(0x54, 0xD8, 0x0D), Operand(0x5A, 0xD8, 0x05), Operand(0x84, 0xD8, 0x0D),
                Operand(0xDE, 0xD8, 0x0D), Operand(0xE4, 0xD8, 0x05),
                BitConverter.UInt32BitsToSingle(Word(image.AsSpan(start + 0xBF)))));

        float Operand(int at, byte first, byte second)
        {
            Expect(at, [first, second]);
            var value = BitConverter.UInt32BitsToSingle(Word(code.At(Word(image.AsSpan(start + at + 2)), 4)));
            if (!float.IsFinite(value)) throw new InvalidDataException("Nonfinite BUBBLEPRISON operand.");
            return value;
        }
        void Expect(int at, ReadOnlySpan<byte> expected)
        {
            if (!image.AsSpan(start + at).StartsWith(expected))
                throw new InvalidDataException($"Unsupported BUBBLEPRISON Start operand at +0x{at:X}.");
        }
    }
    private static int Unique(byte[] image, ReadOnlySpan<byte> pattern)
    {
        var at = image.AsSpan().IndexOf(pattern);
        if (at < 0 || image.AsSpan(at + 1).IndexOf(pattern) >= 0)
            throw new InvalidDataException("Unique BUBBLEPRISON code anchor unavailable.");
        return at;
    }
    private static uint Word(ReadOnlySpan<byte> bytes) => BinaryPrimitives.ReadUInt32LittleEndian(bytes);
}
