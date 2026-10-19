using System.IO;
using System.Threading.Tasks;
using Sacred.Granny.Abstractions;

namespace Sacred.Engine.Assets;

public sealed partial class AssetManager
{
    internal async Task<WorldModelAsset?> LoadProjectileModelAsync(uint type)
    {
        if (GetItem(type) is not { } item || string.IsNullOrWhiteSpace(item.ModelName)) return null;
        var model = await _modelsPak.LoadModelAsync(Path.GetFileName(item.ModelName), GrnMeshExtractionMode.PrimarySlice).ConfigureAwait(false);
        return model.Mesh is null ? null : new(item, model, CreateModelTextureAliases(model, item), null, null);
    }
}
