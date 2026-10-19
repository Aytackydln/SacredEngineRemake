using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Sacred.Core.GameBin.Balance;

namespace Sacred.Assets.GameBin.Balance;

public static class CreatureBalanceBinReader
{
    public static SacredCreatureBalance Load(string path) => Read(File.ReadAllBytes(path));

    public static SacredCreatureBalance Read(ReadOnlySpan<byte> file)
    {
        if (file.Length < 4) throw new InvalidDataException("Truncated Balance.bin header.");
        var length = BinaryPrimitives.ReadUInt32LittleEndian(file);
        if (length > file.Length - 4 || length < SacredCreatureBalanceLayout.RequiredGlobalBytes)
            throw new InvalidDataException("Balance.bin omits creature initialization tuning.");
        var globals = file.Slice(4, checked((int)length));
        var levels = new SacredCreatureHealthLevel[SacredCreatureBalanceLayout.HealthLevelPairCount];
        for (var i = 0; i < levels.Length; i++)
        {
            var row = globals[(SacredCreatureBalanceLayout.HealthLevelPairsOffset + i * 8)..];
            levels[i] = new(BinaryPrimitives.ReadSingleLittleEndian(row), BinaryPrimitives.ReadSingleLittleEndian(row[4..]));
        }
        var factors = new float[SacredCreatureBalanceLayout.DifficultyCount];
        var percent = new int[factors.Length];
        var additions = new int[factors.Length];
        for (var i = 0; i < factors.Length; i++)
        {
            factors[i] = BinaryPrimitives.ReadSingleLittleEndian(globals[(SacredCreatureBalanceLayout.HealthDifficultyFactorsOffset + i * 4)..]);
            percent[i] = BinaryPrimitives.ReadInt32LittleEndian(globals[(SacredCreatureBalanceLayout.LevelPercentAdditionsOffset + i * 4)..]);
            additions[i] = BinaryPrimitives.ReadInt32LittleEndian(globals[(SacredCreatureBalanceLayout.LevelAdditionsOffset + i * 4)..]);
        }
        return new(levels, factors, MemoryMarshal.Read<SacredSkillCurveLayout>(globals[SacredCreatureBalanceLayout.VitalityCurveOffset..]), percent, additions);
    }
}
