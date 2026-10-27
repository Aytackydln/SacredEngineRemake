using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Sacred.Core.GameBin.Balance;

namespace Sacred.Assets.GameBin.Balance;

public static class CreatureResultBalanceBinReader
{
    public static SacredCreatureResultBalanceLayout Read(ReadOnlySpan<byte> file)
    {
        if (file.Length < 4 || BinaryPrimitives.ReadUInt32LittleEndian(file) > file.Length - 4 ||
            BinaryPrimitives.ReadUInt32LittleEndian(file) < SacredCreatureResultBalanceLayout.Size)
            throw new InvalidDataException("Balance.bin omits creature result tuning.");
        return MemoryMarshal.Read<SacredCreatureResultBalanceLayout>(file[4..]);
    }
}
