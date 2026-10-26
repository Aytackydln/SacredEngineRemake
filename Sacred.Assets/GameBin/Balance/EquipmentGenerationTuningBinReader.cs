using Sacred.Core.GameBin.Balance;

namespace Sacred.Assets.GameBin.Balance;

public static class EquipmentGenerationTuningBinReader
{
    public static SacredEquipmentGenerationTuning Read(ReadOnlySpan<byte> file) => new(
        EquipmentGenerationBalanceBinReader.Read(file), EquipmentGenerationBalanceBinReader.ReadBonusCurves(file),
        EquipmentNormalizationBalanceBinReader.Read(file), EquipmentSocketBalanceBinReader.Read(file),
        EquipmentPricingBalanceBinReader.Read(file), EquipmentMagicBalanceBinReader.Read(file), LootBalanceBinReader.Read(file));
}
