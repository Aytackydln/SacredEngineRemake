using System.Collections.Frozen;
using System.Text;
using Sacred.Core;
using Sacred.Core.Pak.Items;
using Sacred.Core.Pak.Weapon;

namespace Sacred.Assets.Paks.Weapon;

public static class WeaponPakParser
{
    public static IEnumerable<SacredEquipment> Parse(string filePath, FrozenDictionary<ushort, ItemsPakEntry> items)
    {
        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read);
        using var br = new BinaryReader(fs);

        const string firstBytes = "WPN";
        var headerBytes = br.ReadBytes(3);
        var headerString = Encoding.ASCII.GetString(headerBytes);
        if (headerString != firstBytes)
        {
            throw new InvalidDataException($"Invalid file format. Expected header '{firstBytes}', but got '{headerString}'.");
        }
        var sacredFile = new SacredPakFile(filePath, SacredPakFileType.Weapon);
        
        br.BaseStream.Seek(0x03, SeekOrigin.Begin);
        var entryCount = br.ReadUInt16(); // Number of entries, 2 bytes at offset 0x03

        br.BaseStream.Seek(0x102, SeekOrigin.Begin);

        for (ushort i = 0; i < entryCount; i++)
        {
            var weapon = SacredEquipment.FromBytes(br, sacredFile, items);
            yield return weapon;
        }
    }

}