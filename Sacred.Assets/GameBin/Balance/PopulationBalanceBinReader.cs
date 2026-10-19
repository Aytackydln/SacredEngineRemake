using System.Buffers.Binary;
using Sacred.Core.GameBin.Balance;

namespace Sacred.Assets.GameBin.Balance;

public sealed record SacredPopulationBalance(float FirstThresholdSeconds, float SubsequentThresholdSeconds);

public static class PopulationBalanceBinReader
{
    public static SacredPopulationBalance Load(string path)
    {
        var file = File.ReadAllBytes(path);
        if (file.Length < 4) throw new InvalidDataException("Missing Balance.bin header.");
        var count = BinaryPrimitives.ReadUInt32LittleEndian(file);
        if (count > file.Length - 4 || count < SacredPopulationBalanceLayout.SubsequentThresholdOffset + 4)
            throw new InvalidDataException("Missing original population timing values.");
        var first = BinaryPrimitives.ReadSingleLittleEndian(file.AsSpan(4 + SacredPopulationBalanceLayout.FirstThresholdOffset));
        var subsequent = BinaryPrimitives.ReadSingleLittleEndian(file.AsSpan(4 + SacredPopulationBalanceLayout.SubsequentThresholdOffset));
        if (!float.IsFinite(first) || !float.IsFinite(subsequent)) throw new InvalidDataException("Invalid population timing values.");
        return new(first, subsequent);
    }
}
