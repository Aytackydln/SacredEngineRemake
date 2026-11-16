using Sacred.Core.Executable;
using Sacred.Core.Executable.Layouts;

namespace Sacred.Assets.Executable;

public static class SacredSettingsSaveDefinitions
{
    public static SacredSettingsSaveDefinition Read(byte[] image)
    {
        using var pe = new SacredSessionExecutableImage(image);
        var saver = pe.Address(pe.FindUnique(SacredExecutableSettingsSaveLayout.Signature) - SacredExecutableSettingsSaveLayout.SignatureOffset);
        return new(pe.Literal(saver, SacredExecutableSettingsSaveLayout.OpenModeOperand, 0x68),
            pe.Literal(saver, SacredExecutableSettingsSaveLayout.RecordFormatOperand, 0x68));
    }
    public static SacredSettingsSaveDefinition Load(string directory)
    {
        Exception? failure = null;
        foreach (var name in new[] { "Sacred.exe", "pureHD.exe" })
        {
            var path = Path.Combine(directory, name); if (!File.Exists(path)) continue;
            try { return Read(File.ReadAllBytes(path)); } catch (InvalidDataException error) { failure = error; }
        }
        throw new InvalidDataException("Original settings save definitions unavailable.", failure);
    }
}
