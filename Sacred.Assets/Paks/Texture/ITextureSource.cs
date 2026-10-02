namespace Sacred.Assets.Paks.Texture;

/// <summary>Loads game textures without prescribing retention or ownership.</summary>
public interface ITextureSource
{
    Task<TextureAsset> LoadTextureAsync(string textureName, CancellationToken cancellationToken = default);
    Task<TextureAsset> LoadTextureAsync(uint entryId, CancellationToken cancellationToken = default);
    bool TryResolveTextureName(string textureName, out string resolvedName);
    bool TryGetTextureName(uint entryId, out string textureName);
    bool TryResolveTextureRecord(string textureName, out TexturePakRecord record);
}
