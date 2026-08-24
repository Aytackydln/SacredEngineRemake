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

        for (ushort i = 0; i < header.EntryCount; i++)
        {
            var weapon = SacredEquipment.FromBytes(br, items);
            yield return weapon;
        }
    }
}
