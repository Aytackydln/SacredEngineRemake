using System.Numerics;
using Sacred.Assets.Paks.Models;
using Sacred.Core.Pak.Items;
using Sacred.Core.Pax.Hero.Objects;
using Sacred.Granny.Animation;
using Sacred.Granny.Assets;

namespace Sacred.GameplayHost.Networking;

/// <summary>The original body and exact saved motion needed by 5559E0/44C330.
/// CPU resources only: loading them does not execute creature activation,
/// restore animation timing, set ModelLoaded or grant actor readiness.</summary>
public sealed class WorldHostHeroModelResources
{
    private WorldHostHeroModelResources(string modelName, uint texture, GrnAsset body,
        GrnAnimationClip animation, Vector3 root, bool hasRoot)
        => (ModelName, Texture, Body, SavedAnimation, RootLocalOffset, HasRoot) =
            (modelName, texture, body, animation, root, hasRoot);

    public string ModelName { get; }
    public uint Texture { get; }
    public GrnAsset Body { get; }
    public GrnAnimationClip SavedAnimation { get; }
    /// <summary>44CA30("__Root", ..., 1): native model coordinates, before
    /// sequence position/orientation. Zero also represents a missing root.</summary>
    public Vector3 RootLocalOffset { get; }
    public bool HasRoot { get; }
    public bool IsInvalidated { get; private set; }
    internal void Invalidate() => IsInvalidated = true;

    internal static async Task<WorldHostHeroModelResources> LoadAsync(string installation,
        ItemsPakEntry descriptor, SacredSavedAnimationRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!request.RequiresActivation || request.Selection > byte.MaxValue)
            throw new NotSupportedException("Host model resources require a nonzero saved per-model motion slot in 1..255.");
        if (string.IsNullOrWhiteSpace(descriptor.ModelName))
            throw new InvalidDataException("Selected hero has no original model name.");
        using var models = ModelsPakArchive.Load(Path.Combine(installation, "pak", "Models.pak"),
            Path.Combine(installation, "pak", "Models.tmp"));
        // Saved equipment is imported after this physical activation call. Do
        // not substitute the default clip or attach the saved inventory early.
        var body = await models.LoadCharacterBaseModelAsync(descriptor.ModelName, [], cancellationToken).ConfigureAwait(false);
        if (body.Mesh is null || body.Skin is null)
            throw new InvalidDataException("Selected host model requires its original mesh and skeleton.");
        var animation = await models.LoadModelAnimationAsync(descriptor.ModelName, (byte)request.Selection, cancellationToken).ConfigureAwait(false)
            ?? throw new NotSupportedException("Selected saved motion is unavailable; native fallback/animation selection is still required.");
        var hasRoot = body.Skin.Skeleton.TryFindBone("__Root", out var rootIndex);
        var root = hasRoot ? body.Skin.Skeleton.Bones[rootIndex].RestLocal.Translation : Vector3.Zero;
        // The shared parser retains authored skeleton coordinates. The original
        // Granny model transform reflects Y before its local bone-state query.
        root.Y = -root.Y;
        cancellationToken.ThrowIfCancellationRequested();
        return new(descriptor.ModelName, descriptor.ModelDesc.TextureId, body, animation, root, hasRoot);
    }
}
