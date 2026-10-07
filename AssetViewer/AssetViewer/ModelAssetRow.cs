using Sacred.Core.Pak.Items;

namespace AssetViewer.AssetViewer;

public sealed class ModelAssetRow(ItemsPakEntry item)
{
    internal ItemsPakEntry Item { get; } = item;
    public ushort EntryId => Item.ItemIndex;
    public uint ResourceId => Item.ModelDesc.ResourceId;
    public string Model => Item.ModelName;
    public SacredItemCategory Category => Item.ModelDesc.Category;
    public SacredItemGraphicType GraphicType => Item.ModelDesc.GraphicType;
    public SacredItemGraphicFlags Flags => Item.ModelDesc.GraphicFlags;
    public uint TextureId => Item.ModelDesc.TextureId;
    public uint EffectTextureId => Item.ModelDesc.EffectTextureId;
    public ushort CreatureEntry => Item.ModelDesc.CreatureEntry;
}
