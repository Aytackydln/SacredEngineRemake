using System.Buffers.Binary;
using Sacred.Core.GameBin.Balance;

namespace Sacred.Assets.GameBin.Balance;

public static class CreatureMarkerBalanceBinReader
{
    public static SacredCreatureMarkerBalance Load(string path) => Read(File.ReadAllBytes(path));
    public static SacredCreatureMarkerBalance Read(ReadOnlySpan<byte> file)
    {
        if (file.Length < 4) throw new InvalidDataException("Truncated Balance.bin header.");
        var count = BinaryPrimitives.ReadUInt32LittleEndian(file);
        if (count < SacredCreatureMarkerBalanceLayout.RequiredGlobalBytes || count > file.Length - 4)
            throw new InvalidDataException("Balance.bin omits creature marker tuning.");
        var globals = file.Slice(4, (int)count);
        var result = new SacredCreatureMarkerBalance(
            BinaryPrimitives.ReadUInt32LittleEndian(globals[SacredCreatureMarkerBalanceLayout.PlayerHealthColorOffset..]),
            BinaryPrimitives.ReadUInt32LittleEndian(globals[SacredCreatureMarkerBalanceLayout.PlayerBorderColorOffset..]),
            BinaryPrimitives.ReadSingleLittleEndian(globals[SacredCreatureMarkerBalanceLayout.MinimumLevelRatioOffset..]),
            BinaryPrimitives.ReadSingleLittleEndian(globals[SacredCreatureMarkerBalanceLayout.RedLevelRatioOffset..]),
            BinaryPrimitives.ReadSingleLittleEndian(globals[SacredCreatureMarkerBalanceLayout.FullExperienceLevelRatioOffset..]));
        if (!float.IsFinite(result.MinimumLevelRatio) || !float.IsFinite(result.RedLevelRatio) ||
            !float.IsFinite(result.FullExperienceLevelRatio) || result.MinimumLevelRatio >= result.FullExperienceLevelRatio ||
            result.FullExperienceLevelRatio >= result.RedLevelRatio)
            throw new InvalidDataException("Invalid creature marker level thresholds.");
        return result;
    }
}
