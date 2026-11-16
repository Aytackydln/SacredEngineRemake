using System.Buffers.Binary;
using Sacred.Core.Executable;
using Sacred.Core.Executable.Layouts;

namespace Sacred.Assets.Executable;

public static class SacredRegionFunctionBindingDefinitions
{
    public static SacredRegionFunctionBindingDefinition Read(byte[] image)
    {
        using var pe = new SacredSessionExecutableImage(image);
        var at = pe.FindUnique(SacredExecutableRegionFunctionBindingLayout.Signature);
        var start = pe.Address(at);
        var first = pe.Literal(start, SacredExecutableRegionFunctionBindingLayout.FirstPrefixOperand, 0xbf);
        var second = pe.Literal(start, SacredExecutableRegionFunctionBindingLayout.SecondPrefixOperand, 0xbf);
        if (first.Length != SacredExecutableRegionFunctionBindingLayout.PrefixLength || second.Length != first.Length)
            throw new InvalidDataException("Unknown original region function prefixes.");
        var classifier = pe.FollowCall(at + SacredExecutableRegionFunctionBindingLayout.DigitCallOffset);
        if (!pe.Bytes(classifier, 2).SequenceEqual<byte>([0x83,0x3d]) || pe.Word(pe.Word(classifier + 2)) != 1 ||
            !pe.Bytes(classifier + 0x1b, 2).SequenceEqual<byte>([0x8b,0x0d]))
            throw new InvalidDataException("Region bindings require the original single-byte class path.");
        var table = pe.Word(pe.Word(classifier + SacredExecutableRegionFunctionBindingLayout.ClassPointerOperand));
        var bytes = pe.Bytes(table - 256, 512);
        var classes = new ushort[SacredExecutableRegionFunctionBindingLayout.SignedClassCount];
        for (var index = 0; index < classes.Length; index++) classes[index] = BinaryPrimitives.ReadUInt16LittleEndian(bytes[(index * 2)..]);
        return new(first, second, classes);
    }
    public static SacredRegionFunctionBindingDefinition Load(string directory)
    {
        Exception? failure = null;
        foreach (var name in new[] { "Sacred.exe", "pureHD.exe" })
        {
            var path = Path.Combine(directory, name); if (!File.Exists(path)) continue;
            try { return Read(File.ReadAllBytes(path)); } catch (InvalidDataException error) { failure = error; }
        }
        throw new InvalidDataException("Original region function-binding definitions unavailable.", failure);
    }
}
