using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Sacred.GameplayHost.World;
using Sacred.Granny.Abstractions;

namespace Sacred.Engine.Assets;

public sealed partial class AssetManager
{
    internal async Task<WorldModelAsset?> LoadDroppedItemModelAsync(GameplayWorldObject obj,
        CancellationToken cancellationToken = default)
    {
        var item = obj.Definition;
        if (string.IsNullOrWhiteSpace(item.ModelName)) return null;
        var model = await _modelsPak.LoadModelAsync(Path.GetFileName(item.ModelName),
            GrnMeshExtractionMode.PrimarySlice, cancellationToken).ConfigureAwait(false);
        if (model.Mesh is null) return null;
        // Dropped equipment renders its model and archive-selected world sparkles.
        // Equipped particle attachments must not be instantiated for ground items.
        return new(item, model, CreateModelTextureAliases(model, item), null, null);
    }
}
