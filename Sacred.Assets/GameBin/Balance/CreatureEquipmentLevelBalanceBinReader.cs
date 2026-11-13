using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Sacred.Core.GameBin.Balance;

namespace Sacred.Assets.GameBin.Balance;

public static class CreatureEquipmentLevelBalanceBinReader
{
    public static SacredCreatureEquipmentLevelBalanceLayout Read(ReadOnlySpan<byte> file)
    {
        if (file.Length < 4 || BinaryPrimitives.ReadUInt32LittleEndian(file) > file.Length - 4 ||
            BinaryPrimitives.ReadUInt32LittleEndian(file) < SacredCreatureEquipmentLevelBalanceLayout.Size)
            throw new InvalidDataException("Balance.bin omits creature equipment-level tuning.");
        return MemoryMarshal.Read<SacredCreatureEquipmentLevelBalanceLayout>(file[4..]);
    }
}
