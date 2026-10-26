using System.Buffers.Binary;
using Sacred.Core.GameBin.Balance;

namespace Sacred.Assets.GameBin.Balance;

public static class EquipmentNormalizationBalanceBinReader
{
    public static SacredEquipmentNormalizationBalance Read(ReadOnlySpan<byte> file)
    {
        if (file.Length < 4) throw new InvalidDataException("Truncated Balance.bin header.");
        var size = BinaryPrimitives.ReadUInt32LittleEndian(file);
        if (size > file.Length - 4 || size < SacredEquipmentGenerationBalanceLayout.HeroClassNormalizationFactorOffset + 4)
            throw new InvalidDataException("Balance.bin omits equipment normalization factors.");
        var globals = file[4..];
        return new(BinaryPrimitives.ReadSingleLittleEndian(globals[SacredEquipmentGenerationBalanceLayout.OtherOpponentNormalizationFactorOffset..]),
            BinaryPrimitives.ReadSingleLittleEndian(globals[SacredEquipmentGenerationBalanceLayout.HeroOpponentNormalizationFactorOffset..]),
            BinaryPrimitives.ReadSingleLittleEndian(globals[SacredEquipmentGenerationBalanceLayout.HeroClassNormalizationFactorOffset..]));
    }
}
