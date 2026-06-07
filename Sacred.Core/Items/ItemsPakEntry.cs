namespace Sacred.Core.Items;

public readonly record struct ItemsPakEntry(
    ItemsPakEntryInfo EntryInfo,
    ItemsPakEntryModelDesc ModelDesc
)
{
    public ushort ItemIndex => EntryInfo.ItemIndex;
    public uint MixedBaseGroupId => ModelDesc.MixedBaseGroupId;
    public uint GraphicRenderFlags => ModelDesc.GraphicRenderFlags;
    public byte RenderClass => ModelDesc.RenderClass;
    public uint ItemId => ModelDesc.ItemId;
}
