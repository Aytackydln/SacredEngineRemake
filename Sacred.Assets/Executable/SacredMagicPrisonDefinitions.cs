using System.Buffers.Binary;
using System.Collections.ObjectModel;
using Sacred.Core.Runtime;

namespace Sacred.Assets.Executable;

/// <summary>Original MAGICPRISON constructor/Start operands. Resource identity,
/// palette coefficients and parameter words are read from code, never snapshots.</summary>
public sealed class SacredMagicPrisonDefinitions
{
    private readonly IReadOnlyDictionary<int, uint> _words;
    private readonly float _indexScale, _greyScale, _blueScale, _low, _medium, _high, _multiplier;
    private SacredMagicPrisonDefinitions(SacredObjectFxInitialDefinition fx, IReadOnlyDictionary<int, uint> words,
        float indexScale, float greyScale, float blueScale, float low, float medium, float high, float multiplier, byte defaultDetailLevel)
    {
        Fx = fx;
        DefaultDetailLevel = defaultDetailLevel;
        _words = new ReadOnlyDictionary<int, uint>(new Dictionary<int, uint>(words));
        (_indexScale, _greyScale, _blueScale, _low, _medium, _high, _multiplier) =
            (indexScale, greyScale, blueScale, low, medium, high, multiplier);
    }
    public uint Resource => Fx.Resource;
    public SacredObjectFxInitialDefinition Fx { get; }
    public byte DefaultDetailLevel { get; }
    public IReadOnlyDictionary<int, uint> InitialWords => _words;
    public uint[] CreatePalette() => SacredMagicPrisonInitialization.CreatePalette(256, _indexScale, _greyScale, _blueScale);
    public uint RateBits(byte detailLevel) => SacredMagicPrisonInitialization.RateBits(detailLevel, _low, _medium, _high, _multiplier);

    public static SacredMagicPrisonDefinitions Load(string directory)
    {
        Exception? failure = null;
        foreach (var name in new[] { "Sacred.exe", "pureHD.exe" })
        {
            var path = Path.Combine(directory, name);
            if (!File.Exists(path)) continue;
            try { return Read(File.ReadAllBytes(path)); }
            catch (InvalidDataException error) { failure = error; }
        }
        throw new InvalidDataException("Original MAGICPRISON initialization unavailable.", failure);
    }

    public static SacredMagicPrisonDefinitions Read(byte[] image)
    {
        var code = new SacredNativeTypeSwitch(image);
        var start = Unique(image, [0x51, 0x53, 0x55, 0x56, 0x8B, 0xF1, 0x57, 0x33, 0xDB,
            0xB9, 8, 0, 0, 0, 0x33, 0xC0, 0x8D, 0xBE, 0xA8, 0x24, 0, 0]);
        Expect(0x16, [0x89, 0x9E, 0xA0, 0x20, 0, 0, 0xC6, 0x86, 0xA4, 0x20, 0, 0, 1]);
        Expect(0x23, [0x8D, 0x96, 0xC8, 0x24, 0, 0, 0xF3, 0xAB, 0xB9, 0x19, 0, 0, 0, 0x8B, 0xFA, 0xF3, 0xAB]);
        Expect(0x57, [0xA0]); // The input pointer is checked against the Start store below.
        var detailAddress = Word(image.AsSpan(start + 0x58));
        Expect(0x67, [0x3C, 2]);
        Expect(0x99, [0x75, 8]);
        Expect(0xA3, [0x3C, 1, 0x75, 8]);
        Expect(0xCD, [0xDB, 0x44, 0x24, 0x10]);
        Expect(0xD7, [0xD9, 0xC0]);
        Expect(0xEC, [0xC1, 0xE5, 8, 0x0B, 0xE8, 0xC1, 0xE5, 8]);
        Expect(0x101, [0x81, 0xFF, 0, 1, 0, 0]);
        var startPointer = new byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(startPointer, checked((uint)code.Address(start)));
        var table = Unique(image, startPointer) - 16;
        var assignmentPattern = new byte[6]; assignmentPattern[0] = 0xC7; assignmentPattern[1] = 0x06;
        BinaryPrimitives.WriteUInt32LittleEndian(assignmentPattern.AsSpan(2), checked((uint)code.Address(table)));
        var constructor = Unique(image, assignmentPattern) - 0x3E;
        if (!image.AsSpan(constructor + 0x26).StartsWith<byte>([0x51, 0x68]) ||
            !image.AsSpan(constructor + 0x2C).StartsWith<byte>([0x8B, 0xCE, 0xE8]))
            throw new InvalidDataException("Unsupported MAGICPRISON constructor/resource linkage.");
        // Configuration constructor assigns detail two. Both Start and the
        // settings loader use this same byte; settings parsing is separate.
        var defaultPattern = new byte[] { 0xB0, 2, 0xC7, 0x05, 0, 0, 0, 0, 0, 0, 0x80, 0x3E,
            0xA2, 0, 0, 0, 0, 0xA2, 0, 0, 0, 0 };
        BinaryPrimitives.WriteUInt32LittleEndian(defaultPattern.AsSpan(4), detailAddress + 8);
        BinaryPrimitives.WriteUInt32LittleEndian(defaultPattern.AsSpan(13), detailAddress + 1);
        BinaryPrimitives.WriteUInt32LittleEndian(defaultPattern.AsSpan(18), detailAddress);
        var defaults = Unique(image, defaultPattern);
        var words = new Dictionary<int, uint>();
        var two = Immediate(0x34, 0xB8);
        var fifty = Immediate(0x5C, 0xB9);
        Expect(0x39, [0x89, 0x9E, 0, 0x25, 0, 0, 0x89, 0x9E, 4, 0x25, 0, 0,
            0x89, 0x86, 0x0C, 0x25, 0, 0, 0x89, 0x86, 0x10, 0x25, 0, 0, 0x89, 0x86, 0xD4, 0x24, 0, 0]);
        Expect(0x61, [0x89, 0x8E, 8, 0x25, 0, 0]);
        Expect(0x7D, [0x89, 0x1A]);
        Expect(0x89, [0x89, 0x8E, 0x14, 0x25, 0, 0]);
        Expect(0xBD, [0x8D, 0x9E, 0xA8, 0x20, 0, 0]);
        Expect(0xC7, [0xD9, 0x9E, 0x20, 0x25, 0, 0]);
        foreach (var offset in new[] { 0x250C, 0x2510, 0x24D4 }) words.Add(offset - 0x20A0, two);
        foreach (var offset in new[] { 0x2508, 0x2514 }) words.Add(offset - 0x20A0, fifty);
        foreach (var (at, nativeOffset) in new[] { (0x69, 0x24AC), (0x73, 0x24B0), (0x7F, 0x24CC), (0x8F, 0x24D0) })
        {
            var store = new byte[6]; store[0] = 0xC7; store[1] = 0x86;
            BinaryPrimitives.WriteInt32LittleEndian(store.AsSpan(2), nativeOffset);
            Expect(at, store); words.Add(nativeOffset - 0x20A0, Word(image.AsSpan(start + at + 6)));
        }
        return new(SacredObjectFxInitialDefinition.Read(image, Word(image.AsSpan(constructor + 0x28))), words,
            FloatOperand(0xD1, 0xD8, 0x0D), FloatOperand(0xD9, 0xD8, 0x0D), FloatOperand(0xE4, 0xD8, 0x0D),
            FloatOperand(0xAF, 0xD9, 0x05), FloatOperand(0xA7, 0xD9, 0x05), FloatOperand(0x9B, 0xD9, 0x05),
            FloatOperand(0xB5, 0xD8, 0x0D), image[defaults + 1]);

        uint Immediate(int at, byte opcode) { Expect(at, [opcode]); return Word(image.AsSpan(start + at + 1)); }
        float FloatOperand(int at, byte first, byte second)
        {
            Expect(at, [first, second]);
            var value = BitConverter.UInt32BitsToSingle(Word(code.At(Word(image.AsSpan(start + at + 2)), 4)));
            if (!float.IsFinite(value)) throw new InvalidDataException("Nonfinite MAGICPRISON initialization operand.");
            return value;
        }
        void Expect(int at, ReadOnlySpan<byte> expected)
        {
            if (!image.AsSpan(start + at).StartsWith(expected))
                throw new InvalidDataException($"Unsupported MAGICPRISON Start operand at +0x{at:X}.");
        }
    }

    private static int Unique(byte[] image, ReadOnlySpan<byte> pattern)
    {
        var at = image.AsSpan().IndexOf(pattern);
        if (at < 0 || image.AsSpan(at + 1).IndexOf(pattern) >= 0)
            throw new InvalidDataException("Unique MAGICPRISON code anchor unavailable.");
        return at;
    }
    private static uint Word(ReadOnlySpan<byte> bytes) => BinaryPrimitives.ReadUInt32LittleEndian(bytes);
}
