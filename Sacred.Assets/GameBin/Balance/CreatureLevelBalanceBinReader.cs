using System.Buffers.Binary;
using Sacred.Core.GameBin.Balance;

namespace Sacred.Assets.GameBin.Balance;

public static class CreatureLevelBalanceBinReader
{
    public static SacredCreatureLevelBalance Load(string path) => Read(File.ReadAllBytes(path));

    public static SacredCreatureLevelBalance Read(ReadOnlySpan<byte> file)
    {
        if (file.Length < 4) throw new InvalidDataException("Truncated Balance.bin header.");
        var length = BinaryPrimitives.ReadUInt32LittleEndian(file);
        if (length > file.Length - 4 || length < SacredCreatureLevelBalanceLayout.RequiredGlobalBytes)
            throw new InvalidDataException("Balance.bin omits creature level-selection tuning.");
        var globals = file.Slice(4, checked((int)length));
        var additions = new uint[SacredCreatureBalanceLayout.DifficultyCount];
        var maximums = new uint[additions.Length];
        for (var i = 0; i < additions.Length; i++)
        {
            additions[i] = BinaryPrimitives.ReadUInt32LittleEndian(globals[(SacredCreatureLevelBalanceLayout.InputAdditionsOffset + i * 4)..]);
            maximums[i] = BinaryPrimitives.ReadUInt32LittleEndian(globals[(SacredCreatureLevelBalanceLayout.MaximumLevelsOffset + i * 4)..]);
        }
        return new(additions,
            BinaryPrimitives.ReadUInt32LittleEndian(globals[SacredCreatureLevelBalanceLayout.MultiplayerAdditionOffset..]),
            BinaryPrimitives.ReadUInt32LittleEndian(globals[SacredCreatureLevelBalanceLayout.BehaviorCapAdditionOffset..]),
            BinaryPrimitives.ReadUInt32LittleEndian(globals[SacredCreatureLevelBalanceLayout.ActorCapAdditionOffset..]), maximums);
    }
}
