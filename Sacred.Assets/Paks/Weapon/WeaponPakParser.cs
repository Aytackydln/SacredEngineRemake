using System.Collections.Frozen;
using Sacred.Assets.Utils;
using Sacred.Core.Pak.Items;
using Sacred.Core.Pak.Weapon;
using Sacred.Core.Utils;

namespace Sacred.Assets.Paks.Weapon;

public static class WeaponPakParser
{
    public static IEnumerable<SacredEquipment> Parse(string filePath, FrozenDictionary<ushort, ItemsPakEntry> items)
    {
        using var stopwatch = new LoggingStopwatch("Loading Weapons.pak... ");
        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read);
        using var br = new BinaryReader(fs);

        var header = br.ReadStruct<WeaponPakHeaderLayout>(WeaponPakHeaderLayout.SerializedSize);
        header.ValidateSignature();

        if (header.EntryCount > (fs.Length - fs.Position) / SacredEquipmentLayout.Size)
            throw new InvalidDataException("Weapon.pak equipment table is outside the file bounds.");

        var equipment = new List<SacredEquipment>(checked((int)header.EntryCount));
        for (uint i = 0; i < header.EntryCount; i++)
            equipment.Add(SacredEquipment.FromBytes(br, items));
        return SacredEquipmentVisualResolver.Resolve(items, equipment);
    }
}
