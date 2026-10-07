using Sacred.Assets.Paks.Texture;
using Sacred.Core.Pak.Texture;

namespace AssetViewer.AssetViewer;

public sealed class TextureAssetRow(TexturePakEntry entry)
{
    internal TexturePakEntry Entry { get; } = entry;
    public string Archive => Entry.ArchiveName;
    public uint EntryId => Entry.EntryId;
    public string Name => Entry.Record.Name;
    public ushort Width => Entry.Record.Width;
    public ushort Height => Entry.Record.Height;
    public SacredTextureStorageFormat Format => Entry.Record.StorageFormat;
    public long Offset => Entry.Record.Offset;
    public int Size => Entry.Record.Size;
}
