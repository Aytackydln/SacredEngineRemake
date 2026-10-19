using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Sacred.Core.GameBin.Balance;

namespace Sacred.Assets.GameBin.Balance;

public static class EquipmentGenerationBalanceBinReader
{
    public static SacredEquipmentGenerationBalance Load(string path) => Read(File.ReadAllBytes(path));

    public static SacredEquipmentGenerationBalance Read(ReadOnlySpan<byte> file)
    {
        var globals = Globals(file);
        return new(
            BinaryPrimitives.ReadSingleLittleEndian(globals[SacredEquipmentGenerationBalanceLayout.LevelFactorZeroOffset..]),
            BinaryPrimitives.ReadSingleLittleEndian(globals[SacredEquipmentGenerationBalanceLayout.LevelFactorHundredOffset..]),
            BinaryPrimitives.ReadSingleLittleEndian(globals[SacredEquipmentGenerationBalanceLayout.LevelScaleOffset..]));
    }

    public static SacredEquipmentBonusBalance ReadBonusCurves(ReadOnlySpan<byte> file)
    {
        var globals = Globals(file);
        var curves = MemoryMarshal.Cast<byte, SacredEquipmentBonusCurveLayout>(globals.Slice(
            SacredEquipmentGenerationBalanceLayout.IndividualBonusCurvesOffset,
            SacredEquipmentBonusBalance.IndividualCount * SacredEquipmentBonusCurveLayout.Size)).ToArray();
        return new(curves,
            MemoryMarshal.Read<SacredEquipmentBonusCurveLayout>(globals[SacredEquipmentGenerationBalanceLayout.SpecialMoveBonusCurveOffset..]),
            MemoryMarshal.Read<SacredEquipmentBonusCurveLayout>(globals[SacredEquipmentGenerationBalanceLayout.SpellBonusCurveOffset..]),
            MemoryMarshal.Read<SacredEquipmentBonusCurveLayout>(globals[SacredEquipmentGenerationBalanceLayout.SkillBonusCurveOffset..]),
            MemoryMarshal.Read<SacredEquipmentBonusCurveLayout>(globals[SacredEquipmentGenerationBalanceLayout.OtherBonusCurveOffset..]));
    }

    private static ReadOnlySpan<byte> Globals(ReadOnlySpan<byte> file)
    {
        if (file.Length < 4) throw new InvalidDataException("Truncated Balance.bin header.");
        var size = BinaryPrimitives.ReadUInt32LittleEndian(file);
        if (size > file.Length - 4 || size < SacredEquipmentGenerationBalanceLayout.RequiredGlobalBytes)
            throw new InvalidDataException("Balance.bin omits equipment generation tuning.");
        return file.Slice(4, checked((int)size));
    }
}
