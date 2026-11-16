using Sacred.Core.Executable;
using Sacred.Core.Executable.Layouts;

namespace Sacred.Assets.Executable;

public static class SacredSettingsFileDefinitions
{
    public static SacredSettingsFileDefinition Read(byte[] image)
    {
        using var pe = new SacredSessionExecutableImage(image);
        var loader = pe.Address(pe.FindUnique(SacredExecutableSettingsFileLayout.LoaderSignature) - SacredExecutableSettingsFileLayout.LoaderSignatureOffset);
        var callers = pe.Callers(loader).Where(at =>
        {
            if (at < -SacredExecutableSettingsFileLayout.CallerDelimiterScanOperand) return false;
            var address = pe.Address(at);
            return HasRange(address, SacredExecutableSettingsFileLayout.CallerFileScanOperand) && HasRange(address, SacredExecutableSettingsFileLayout.CallerDelimiterScanOperand);
        }).ToArray();
        if (callers.Length != 1) throw new InvalidDataException("Original settings-file caller missing or ambiguous.");
        var caller = pe.Address(callers[0]);
        var writer = pe.Address(pe.FindUnique(SacredExecutableSettingsFileLayout.IntegerWriterSignature) - SacredExecutableSettingsFileLayout.IntegerWriterSignatureOffset);
        var skip = new List<byte>();
        foreach (var operand in SacredExecutableSettingsFileLayout.SkipPrefixOperands)
        {
            if (!pe.Bytes(loader + (uint)operand - 2, 2).SequenceEqual<byte>([0x80, 0x38])) throw new InvalidDataException("Unknown original settings skip-prefix instruction.");
            skip.Add(pe.Bytes(loader + (uint)operand, 1)[0]);
        }
        return new(Range(caller, SacredExecutableSettingsFileLayout.CallerFileScanOperand),
            Range(caller, SacredExecutableSettingsFileLayout.CallerDelimiterScanOperand),
            pe.Literal(loader, SacredExecutableSettingsFileLayout.OpenModeOperand, 0x68), Capacity(SacredExecutableSettingsFileLayout.FirstCapacityOperand),
            Capacity(SacredExecutableSettingsFileLayout.NextCapacityOperand), skip.ToArray(),
            pe.Literal(loader, SacredExecutableSettingsFileLayout.ValueDelimitersOperand, 0x68),
            pe.Literal(writer, SacredExecutableSettingsFileLayout.IntegerFormatOperand, 0x68));

        int Capacity(int operand)
        {
            if (pe.Bytes(loader + (uint)operand - 1, 1)[0] != 0x6a) throw new InvalidDataException("Unknown original settings line-read capacity.");
            var value = (sbyte)pe.Bytes(loader + (uint)operand, 1)[0];
            if (value is < 2 or > 100) throw new InvalidDataException("Original line-read capacity exceeds established stack buffer.");
            return value;
        }
        bool HasRange(uint address, int operand) => pe.Bytes((uint)(address + operand - 1), 1)[0] == 0xbf &&
            pe.Bytes((uint)(address + operand + SacredExecutableSettingsFileLayout.CallerEndBaseDistance - 2), 2).SequenceEqual<byte>([0x81, 0xc1]) &&
            pe.Bytes((uint)(address + operand + SacredExecutableSettingsFileLayout.CallerRangeStartDistance - 1), 1)[0] == 0x68;
        byte[] Range(uint address, int operand)
        {
            var scan = (uint)(address + operand); var length = pe.Text(pe.Word(scan)).Length;
            var end = checked(pe.Word(scan + SacredExecutableSettingsFileLayout.CallerEndBaseDistance) + (uint)length);
            var begin = pe.Word(scan + SacredExecutableSettingsFileLayout.CallerRangeStartDistance);
            if (end < begin || end - begin > 4096) throw new InvalidDataException("Original settings caller range exceeds compared bounds.");
            return pe.Bytes(begin, (int)(end - begin)).ToArray();
        }
    }
    public static SacredSettingsFileDefinition Load(string directory)
    {
        Exception? failure = null;
        foreach (var name in new[] { "Sacred.exe", "pureHD.exe" })
        {
            var path = Path.Combine(directory, name); if (!File.Exists(path)) continue;
            try { return Read(File.ReadAllBytes(path)); } catch (InvalidDataException error) { failure = error; }
        }
        throw new InvalidDataException("Original settings-file definitions unavailable.", failure);
    }
}
