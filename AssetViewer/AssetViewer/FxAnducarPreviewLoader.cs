using System.Linq;
using System.Threading.Tasks;
using Sacred.Granny.Assets;
using Sacred.Granny.Meshes;
using Sacred.Particles;

namespace AssetViewer.AssetViewer;

internal static class FxAnducarPreviewLoader
{
    public static Task LoadAsync(ModelPreviewPane preview, AssetViewerSession session,
        SacredParticleCatalogue catalogue, string effect, SacredAnducarFxDefinition definition, FxPreviewParameters parameters)
    {
        GrnAsset[] models = [];
        return preview.LoadAsync(effect, async token =>
        {
            models = await Task.WhenAll(definition.ModelNames.Select(name => session.Models.LoadModelAsync(name, cancellationToken: token)));
            return new GrnAsset(effect, [], null, new Mesh([], []));
        }, [], fxFactory: _ => new FxPreviewPlayback(catalogue, definition, models, parameters.Distance ?? 0));
    }
}
