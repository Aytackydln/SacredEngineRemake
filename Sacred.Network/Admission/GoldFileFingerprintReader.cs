using System.Buffers.Binary;
using Sacred.Network.Protocol;

namespace Sacred.Network.Admission;

/// <summary>Gold client loader at 0x803920, including its native optional-archive and reset rules.</summary>
public static class GoldFileFingerprintReader
{
    public static GoldDataFingerprints Read(string gameDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameDirectory);
        Span<uint> values = stackalloc uint[GoldDataFingerprints.Count];
        for (var index = 0; index < values.Length; index++)
        {
            var field = GoldDataFingerprints.Fields[index];
            var path = Path.Combine(gameDirectory, field.RelativePath!);
            values[index] = index == 20 ? ReadWeaponModifiers(path) : ReadFile(path);
        }
        return GoldDataFingerprints.FromFileChecksums(values);
    }

    public static uint ReadFile(string path)
    {
        try
        {
            using var file = File.OpenRead(path);
            return SacredChecksum.Compute(file, file.Length);
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException) { return 0; }
    }

    private static uint ReadWeaponModifiers(string path)
    {
        try
        {
            using var file = File.OpenRead(path);
            Span<byte> header = stackalloc byte[sizeof(uint)];
            file.ReadExactly(header);
            var prefixBytes = checked((long)BinaryPrimitives.ReadUInt32LittleEndian(header) * 224);
            file.Position = 0;
            // The native zero prefix argument selects the entire file.
            return SacredChecksum.Compute(file, prefixBytes == 0 ? file.Length : prefixBytes);
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException) { return 0; }
    }
}
