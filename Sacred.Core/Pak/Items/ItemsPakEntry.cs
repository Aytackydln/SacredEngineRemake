namespace Sacred.Core.Pak.Items;

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
    public uint EffectTextureId => ModelDesc.EffectTextureId;
    public ushort StaticSpriteFrameCount => ModelDesc.StaticSpriteFrameCount;
    public byte StaticSpriteFrameDuration10Ms => (byte)ModelDesc.ModelTransformFlags;
}
