using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Sacred.Core.GameBin.Balance;

namespace Sacred.Assets.GameBin.Balance;

public static class CreatureSkillBalanceBinReader
{
    public static SacredCreatureSkillBalanceLayout Read(ReadOnlySpan<byte> file)
    {
        if (file.Length < 4 || BinaryPrimitives.ReadUInt32LittleEndian(file) > file.Length - 4 ||
            BinaryPrimitives.ReadUInt32LittleEndian(file) < SacredCreatureSkillBalanceLayout.Size)
            throw new InvalidDataException("Balance.bin omits creature passive-skill tuning.");
        return MemoryMarshal.Read<SacredCreatureSkillBalanceLayout>(file[4..]);
    }
}
