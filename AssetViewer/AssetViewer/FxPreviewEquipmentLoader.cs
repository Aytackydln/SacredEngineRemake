using System;
using System.Threading;
using System.Threading.Tasks;
using Sacred.Assets.Paks.Models;
using Sacred.Core.Pak.Weapon;
using Sacred.Granny.Assets;
using Sacred.Particles;

namespace AssetViewer.AssetViewer;

internal sealed record FxPreviewEquipmentModel(SacredEquipment Equipment, GrnAsset Asset,
    FxPreviewAttachmentInputs Inputs, bool Related);

/// <summary>Selects a random compatible authored model, checking required helpers
/// before publishing the preview. Cancellation prevents stale selection results.</summary>
internal static class FxPreviewEquipmentLoader
{
    public static async Task<FxPreviewEquipmentModel?> LoadAsync(ModelsPakArchive models,
        SacredEquipment[] equipment, SacredParticleCatalogue catalogue, SacredParticleDefinition definition,
        SacredModelFxAttachmentDefinition rule, SacredEquipment? previous, CancellationToken token)
    {
        var candidates = FxPreviewEquipment.Candidates(equipment, definition, rule);
        Console.WriteLine($"[Assets] FX weapon candidates: {definition.TypeName}/{definition.Preset}; {candidates.Length} records; helpers={rule.StartBoneName}, {rule.EndBoneName ?? "none"}.");
        Random.Shared.Shuffle(candidates);
        // Equipment contains inline-array layouts; compare archive identities,
        // because their default structural equality is unsupported by .NET.
        if (previous is { } selected && Array.FindIndex(candidates,
                candidate => candidate.IdemId == selected.IdemId) is var index && index >= 0)
            (candidates[0], candidates[index]) = (candidates[index], candidates[0]);
        foreach (var candidate in candidates)
        {
            token.ThrowIfCancellationRequested();
            if (!models.ContainsResource(candidate.Item.ModelName)) continue;
            var asset = await models.LoadModelAsync(candidate.Item.ModelName, cancellationToken: token);
            if (!FxPreviewAttachmentInputs.TryCreate(asset, rule, catalogue.Projection, out var inputs)) continue;
            return new(candidate, asset, inputs!, FxPreviewEquipment.Matches(candidate, definition, rule));
        }
        return null;
    }
}
