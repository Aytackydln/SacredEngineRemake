using Sacred.Core.Executable;
using Sacred.Core.Executable.Layouts;

namespace Sacred.Assets.Executable;

public static class SacredCrtBufferDefinitions
{
    public static SacredCrtBufferDefinition Read(byte[] image)
    {
        using var pe = new SacredSessionExecutableImage(image);
        var function = pe.Address(pe.FindUnique(SacredExecutableCrtBufferLayout.Signature) - SacredExecutableCrtBufferLayout.SignatureOffset);
        if (!pe.Bytes(function + 0x20, 3).SequenceEqual<byte>([0xc7, 0x41, 0x18]) || !pe.Bytes(function + 0x33, 3).SequenceEqual<byte>([0xc7, 0x41, 0x18]))
            throw new InvalidDataException("Unknown original CRT buffer-size instructions.");
        return new(pe.Operand(function, SacredExecutableCrtBufferLayout.AllocationOperand, 0x68),
            pe.Word(function + SacredExecutableCrtBufferLayout.BufferSizeOperand), pe.Word(function + SacredExecutableCrtBufferLayout.FallbackSizeOperand));
    }
    public static SacredCrtBufferDefinition Load(string directory)
    {
        Exception? failure = null;
        foreach (var name in new[] { "Sacred.exe", "pureHD.exe" })
        {
            var path = Path.Combine(directory, name); if (!File.Exists(path)) continue;
            try { return Read(File.ReadAllBytes(path)); } catch (InvalidDataException error) { failure = error; }
        }
        throw new InvalidDataException("Original CRT buffer definitions unavailable.", failure);
    }
}
