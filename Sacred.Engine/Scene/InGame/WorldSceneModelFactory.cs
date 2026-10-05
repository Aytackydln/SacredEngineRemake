using System.Collections.Generic;
using System.Numerics;
using Sacred.Assets.Paks.Texture;
using Sacred.Core.Pak.Items;
using Sacred.Core.World.Sector;
using Sacred.Granny.Meshes;
using Sacred.World.Geometry;

namespace Sacred.Engine.Scene.InGame;

internal static class WorldSceneModelFactory
{
    public static SceneModel Create(
        StaticWorldObject placement,
        ItemsPakEntry item,
        Mesh mesh,
        IReadOnlyDictionary<string, ModelTextureReference> textureAliases,
        Vector3 sourceOriginOffset)
    {
        // Script-created objects retain their absolute tile anchor in the static-object
        // record. ProjectedX/Y are sprite-space values and must never be inverted for a
        // model transform: doing so makes a distant object share the player's 3D area.
        var label = item.ModelDesc.IsSelectable ? "Interactive model" : "World object";
        var model = new SceneModel(
            $"{label}: static {placement.StaticId}, item {item.ItemIndex}, {item.ModelName}",
            mesh,
            new Vector3(WorldModelPose.TilePosition(placement), 0.0f),
            new Vector3(0.0f, 0.0f, WorldModelPose.RotationRadians(
                item.ModelDesc.Angle3D,
                placement.ScriptFacingDegrees)),
            WorldModelPose.Scale,
            textureAliases,
            sourceOriginOffset: sourceOriginOffset,
            blockRadius: item.ModelDesc.BlockRadius) { IsWorldObject = true };
        model.SetPose(model.Position, model.Rotation,
            new Vector2(placement.TileWorldX, placement.TileWorldY));
        model.SetModelProjection(WorldModelPose.CameraProjection);
        return model;
    }

}
