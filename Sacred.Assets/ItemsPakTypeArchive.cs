using System.IO;

namespace Sacred.Assets;

public sealed class ItemsPakTypeArchive
{
    private readonly ItemsPakTypeData _data;

    private ItemsPakTypeArchive(ItemsPakTypeData data) => _data = data;

    public static ItemsPakTypeArchive Load(string path) =>
        new(ItemsPakTypeData.FromBytes(File.ReadAllBytes(path)));

    public ItemTypeRecord? Get(uint typeId) => _data.Get(typeId);
}
