using System.Text;
using Sacred.Assets.Utils;
using Sacred.Core.Pak.Items;
using Sacred.Core.Utils;

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

        var itemsPakHeaderLayout = br.ReadStruct<ItemsPakHeaderLayout>(ItemsPakHeaderLayout.SerializedSize);
        itemsPakHeaderLayout.ValidateSignature();
        var entryCount = itemsPakHeaderLayout.EntryCount;

        var entryInfos = new List<ItemsPakEntryInfo>(entryCount);

        for (ushort entryIndex = 0; entryIndex < entryCount; entryIndex++)
        {
            var entryInfo = ItemsPakEntryInfo.FromBytes(entryIndex, br);

            entryInfos.Add(entryInfo);
        }

        return ItemsPakEntry.ReadMany(pakBytes, entryInfos);
    }

}
