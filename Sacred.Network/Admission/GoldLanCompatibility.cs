using Sacred.Network.Protocol;

namespace Sacred.Network.Admission;

/// <summary>Read-only installation compatibility; a complete verified player/profile request is still required.</summary>
public sealed record GoldLanCompatibility(ushort Version, GoldDataFingerprints Fingerprints,
    uint ExecutableSizeToken, uint ExecutableFingerprint, uint BannerFingerprint)
{
    public static GoldLanCompatibility Read(string gameDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameDirectory);
        var executable = File.ReadAllBytes(Path.Combine(gameDirectory, "Sacred.exe"));
        var tinCat = File.ReadAllBytes(Path.Combine(gameDirectory, "tincat2.dll"));
        var version = GoldNetworkBuildReader.Read(executable, tinCat);
        var fingerprints = GoldFileFingerprintReader.Read(gameDirectory);
        var sizeToken = unchecked((uint)executable.Length + 0x99F2AAF5);
        var executableFingerprint = SacredChecksum.Compute(executable);
        var banner = GoldFileFingerprintReader.ReadFile(Path.Combine(gameDirectory, "Banner.tga"));
        return new(version, fingerprints, sizeToken, executableFingerprint, banner);
    }
}
