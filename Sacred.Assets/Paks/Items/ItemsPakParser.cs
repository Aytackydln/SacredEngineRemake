using System.Text;
using Sacred.Core;
using Sacred.Core.Pak.Items;

namespace Sacred.Assets.Paks.Items;

public static class ItemsPakParser
{
    private static readonly Encoding SacredEncoding = Encoding.GetEncoding("iso-8859-1");

    public static IEnumerable<ItemsPakEntry> Parse(string filePath)
    {
        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read);
        using var br = new BinaryReader(fs, SacredEncoding);

        const string firstBytes = "ITM";
        var headerBytes = br.ReadBytes(3);
        var headerString = Encoding.ASCII.GetString(headerBytes);
        if (headerString != firstBytes)
        {
            throw new InvalidDataException($"Invalid file format. Expected header '{firstBytes}', but got '{headerString}'.");
        }

        var sacredFile = new SacredPakFile(filePath, SacredPakFileType.Items);

        var version = br.ReadByte();
        var entryCount = br.ReadInt32();

        br.BaseStream.Seek(0x102, SeekOrigin.Begin);

        var entryInfos = new List<ItemsPakEntryInfo>(entryCount);

        for (var entryIndex = 0; entryIndex < entryCount; entryIndex++)
        {
            var entryInfo = ItemsPakEntryInfo.FromBytes(checked((ushort)entryIndex), br);

            entryInfos.Add(entryInfo);
        }

        foreach (var entryInfo in entryInfos)
        {
            var pakOffset = entryInfo.ModelDescOffset;

            var modelDesc = ItemsPakEntryModelDesc.FromBytes(sacredFile, pakOffset, br);

            yield return new ItemsPakEntry(
                EntryInfo: entryInfo,
                ModelDesc: modelDesc
            );
        }
    }

}
