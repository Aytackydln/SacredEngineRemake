using System.IO;

namespace Sacred.Assets;

public sealed class ItemsPakModelArchive
{
    private readonly ItemsPakModelData _data;

    private ItemsPakModelArchive(ItemsPakModelData data) => _data = data;

    public static ItemsPakModelArchive Load(string path) =>
        new(ItemsPakModelData.FromBytes(File.ReadAllBytes(path)));

    public PlayerCharacterItemRecord? Get(uint entryId) => _data.Get(entryId);
}
