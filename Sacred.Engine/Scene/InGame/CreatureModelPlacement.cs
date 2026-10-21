using System.Numerics;
using Sacred.Granny.Assets;
using Sacred.World.Geometry;

namespace Sacred.Engine.Scene.InGame;

/// <summary>Initial bind-mesh placement before animation is available. Native sequence
/// placement replaces the bind root's position/orientation; animation supplies its own root.</summary>
internal static class CreatureModelPlacement
{
    public static void Apply(SceneModel model, GrnAsset asset)
    {
        if (asset.Skin is { Skeleton.Bones.Length: > 0 } skin &&
            Matrix4x4.Decompose(skin.Skeleton.Bones[0].RestWorld, out _, out var rotation, out var position) &&
            Matrix4x4.Invert(Matrix4x4.CreateFromQuaternion(rotation) * Matrix4x4.CreateTranslation(position), out var inverseRoot))
        {
            model.SetSourceTransform(inverseRoot);
        }
        model.SetModelProjection(WorldModelPose.CameraProjection);
    }
}
