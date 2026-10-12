using System.Buffers.Binary;

namespace Sacred.Network.Admission;

/// <summary>Verified English Gold client data and its paired TinCat build; no version word is synthesized.</summary>
public static class GoldNetworkBuildReader
{
    // Gold GetVersionValue 0x803870 reads these four seeds; TinCat export is 0x10007690.
    private const uint VersionSeedsRva = 0x61B734;
    private const uint TinCatBuildExportRva = 0x7690;

    public static ushort Read(byte[] sacredExecutable, byte[] tinCatDll)
    {
        ArgumentNullException.ThrowIfNull(sacredExecutable);
        ArgumentNullException.ThrowIfNull(tinCatDll);
        var executable = new GoldPeImage(sacredExecutable);
        var transport = new GoldPeImage(tinCatDll);
        var seeds = executable.At(VersionSeedsRva, 16);
        var export = transport.At(TinCatBuildExportRva, 6);
        if (export[0] != 0xA1 || export[5] != 0xC3)
            throw new InvalidDataException("Unsupported Gold TinCat build-number export.");
        var address = BinaryPrimitives.ReadUInt32LittleEndian(export[1..]);
        if (address < transport.ImageBase || address - transport.ImageBase > uint.MaxValue)
            throw new InvalidDataException("Invalid TinCat build-number data address.");
        var build = BinaryPrimitives.ReadUInt32LittleEndian(transport.At((uint)(address - transport.ImageBase), 4));
        var first = unchecked((byte)(Word(seeds, 0) + 0x46));
        var second = unchecked((byte)(unchecked(Word(seeds, 8) - 0x3FF59A1E) / 10000));
        var third = unchecked((byte)(Word(seeds, 4) - 0x70));
        var fourth = unchecked((byte)(unchecked(Word(seeds, 12) + 0x2B7) >> 8));
        // The native return orders the stored bytes 1, 3, 0, 2.
        var versionValue = (uint)(first * 1000 + third * 100 + second * 10 + fourth);
        if (build == 0 || versionValue < 10)
            throw new InvalidDataException("Invalid Gold network build metadata.");
        var wireValue = checked((ulong)versionValue * build - (ulong)build * 10);
        if (wireValue > ushort.MaxValue)
            throw new InvalidDataException("Gold network version cannot satisfy the native ushort comparison.");
        return (ushort)wireValue;
    }

    private static uint Word(ReadOnlySpan<byte> bytes, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(bytes[offset..]);
}
