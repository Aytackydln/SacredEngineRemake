using Sacred.Core.Executable;
using Sacred.Core.Executable.Layouts;

namespace Sacred.Assets.Executable;

public static class SacredCrtInitialFileStates
{
    public static SacredCrtInitialFileState Read(byte[] image)
    {
        using var pe = new SacredSessionExecutableImage(image);
        var mode = pe.Address(pe.FindUnique(SacredExecutableCrtInitialFileLayout.ModeSignature));
        var open = pe.Address(pe.FindUnique(SacredExecutableCrtInitialFileLayout.OpenSignature));
        var commitPointer = (uint)(mode + SacredExecutableCrtInitialFileLayout.CommitPointerFromSignature);
        if (!pe.Bytes(commitPointer - 2, 2).SequenceEqual<byte>([0x8b, 0x35]) ||
            !pe.Bytes(open + SacredExecutableCrtInitialFileLayout.FileModePointer - 2, 2).SequenceEqual<byte>([0x39, 0x05]) ||
            !pe.Bytes(open + SacredExecutableCrtInitialFileLayout.UmaskPointer - 2, 2).SequenceEqual<byte>([0x8b, 0x0d]))
            throw new InvalidDataException("Unknown original CRT initial-global instructions.");
        return new(pe.Word(pe.Word(commitPointer)), pe.Word(pe.Word(open + SacredExecutableCrtInitialFileLayout.FileModePointer)),
            pe.Word(pe.Word(open + SacredExecutableCrtInitialFileLayout.UmaskPointer)));
    }
    public static SacredCrtInitialFileState Load(string directory)
    {
        Exception? failure = null;
        foreach (var name in new[] { "Sacred.exe", "pureHD.exe" })
        {
            var path = Path.Combine(directory, name); if (!File.Exists(path)) continue;
            try { return Read(File.ReadAllBytes(path)); } catch (InvalidDataException error) { failure = error; }
        }
        throw new InvalidDataException("Original CRT initial file state unavailable.", failure);
    }
}
