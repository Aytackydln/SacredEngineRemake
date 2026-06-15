using System.Text;
using Sacred.Assets.Utils;
using Sacred.Core;
using Sacred.Core.Pak.Items;

namespace Sacred.Assets.Paks.Items;

public static class ItemsPakArchive
{
    private static readonly Encoding SacredEncoding = Encoding.GetEncoding("iso-8859-1");

    public static IEnumerable<ItemsPakEntry> Load(string filePath)
    {
        using var stopwatch = new LoggingStopwatch("Loading Items.pak... ");

        var pakBytes = File.ReadAllBytes(filePath);
        using var ms = new MemoryStream(pakBytes);
        using var br = new BinaryReader(ms, SacredEncoding);

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

        var modelDescIndex = 0;
        foreach (var modelDesc in ItemsPakEntryModelDesc.ReadMany(sacredFile, pakBytes, entryInfos))
        {
            yield return new ItemsPakEntry(
                EntryInfo: entryInfos[modelDescIndex++],
                ModelDesc: modelDesc
            );
        }
    }

}
