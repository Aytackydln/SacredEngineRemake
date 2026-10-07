using System.Collections.Generic;
using System.Numerics;
using Sacred.Granny.Assets;

namespace AssetViewer.AssetViewer;

internal static class AssetPreviewEffects
{
    public static EquipmentEffectScene Create(GrnAsset asset, IReadOnlyList<ModelPreviewVisual> visuals,
        bool compositeSlices)
    {
        var attachments = new List<EquipmentEffectAttachment>();
        var boundsSize = asset.Diagnostics?.WholeModelBounds is { } bounds
            ? Vector3.Distance(bounds.Min, bounds.Max) : 40f;
        // Character extraction produces a base slice followed by one slice per attachment.
        // The models browser renders every slice of the selected source model.
        var count = compositeSlices ? asset.Diagnostics?.Slices.Count ?? 0 : visuals.Count;
        for (var index = 0; index < count; index++)
        {
            var visual = visuals[compositeSlices ? 0 : index];
            var equipment = visual.Equipment;
            attachments.Add(new EquipmentEffectAttachment(index, visual.Item.ModelName,
                visual.RigidAttachBoneName, equipment?.Damage ?? default, boundsSize)
            {
                ItemId = visual.Item.ItemIndex,
                BaseItemId = equipment?.BaseItemId ?? 0,
                BonusTypes = equipment?.BonusTypes ?? default,
                BonusGroups = equipment?.BonusGroups ?? default,
                EquipmentType = equipment?.EquipmentType ?? default,
                ItemEffectSelector = visual.Item.ModelDesc.EffectTextureIndex
            });
        }
        var scene = EquipmentEffectSceneFactory.Create(asset, attachments) ?? EquipmentEffectScene.Empty;
        // Let chains settle and particle emitters fill before fitting the preview camera.
        // Every preview owns its scene, so this does not advance an engine scene.
        if (scene.Mesh is not null)
            for (var step = 0; step < 120; step++) scene.Advance(1f / 60f);
        return scene;
    }
}
