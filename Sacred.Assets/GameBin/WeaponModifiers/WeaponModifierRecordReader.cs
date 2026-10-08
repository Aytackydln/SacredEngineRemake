using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Sacred.Core.GameBin.WeaponModifiers;

namespace Sacred.Assets.GameBin.WeaponModifiers;

internal static class WeaponModifierRecordReader
{
    public static SacredWeaponModifier Read(ReadOnlySpan<byte> data, int recordIndex, int fileOffset, out int bytesRead)
    {
        if (data.Length < SacredWeaponModifierRecordLayout.Size)
            throw new InvalidDataException($"Truncated wpmod.bin record {recordIndex} at offset {fileOffset}.");

        var record = MemoryMarshal.Read<SacredWeaponModifierRecordLayout>(data);
        if (record.Definition.ItemCount is < 0 or > SacredWeaponModifierLayout.MaximumItemCount)
            throw new InvalidDataException($"Invalid wpmod.bin item count in record {recordIndex}.");

        var bonusData = data[SacredWeaponModifierRecordLayout.Size..];
        if (record.BonusCount > bonusData.Length / SacredWeaponModifierBonusLayout.Size)
            throw new InvalidDataException($"Truncated wpmod.bin bonus vector in record {recordIndex}.");

        var bonusCount = (int)record.BonusCount;
        bytesRead = SacredWeaponModifierRecordLayout.Size + bonusCount * SacredWeaponModifierBonusLayout.Size;

        return new SacredWeaponModifier(
            recordIndex,
            fileOffset,
            ReadItemIds(record.Definition, data),
            record.Definition,
            ReadBonuses(bonusData, bonusCount));
    }

    private static uint[] ReadItemIds(SacredWeaponModifierLayout header, ReadOnlySpan<byte> bytes)
    {
        var ids = new uint[header.ItemCount];
        for (var index = 0; index < ids.Length; index++)
            ids[index] = BinaryPrimitives.ReadUInt32LittleEndian(bytes[(index * sizeof(uint))..]);

        return ids;
    }

    private static SacredWeaponModifierBonus[] ReadBonuses(ReadOnlySpan<byte> data, int count)
    {
        var bonuses = new SacredWeaponModifierBonus[count];
        for (var index = 0; index < bonuses.Length; index++)
        {
            var layout = MemoryMarshal.Read<SacredWeaponModifierBonusLayout>(
                data[(index * SacredWeaponModifierBonusLayout.Size)..]);

            bonuses[index] = new SacredWeaponModifierBonus(
                layout.Chance,
                layout.Type,
                layout.Group,
                layout.Minimum,
                layout.Maximum,
                layout.Pool,
                layout.CombatArtLevelDivisor,
                layout.SkillLevelDivisor);
        }

        return bonuses;
    }
}
