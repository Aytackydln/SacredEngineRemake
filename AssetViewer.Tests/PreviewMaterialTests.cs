using System.Numerics;
using AssetViewer.ItemViewer;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sacred.Assets.Paks.Texture;
using Sacred.Granny.Meshes;

namespace AssetViewer.Tests;

[TestClass]
public sealed class PreviewMaterialTests
{
    private static readonly MeshSurface[] Surfaces = [new(0, 3, "stone"), new(3, 3, "fire"), new(6, 3, "glow")];

    [TestMethod]
    public void BinaryCoverageUsesOpaquePassWhileFractionalCoverageRequiresBlending()
    {
        Assert.IsFalse(ModelPreviewMaterials.HasTranslucentPixels([255, 255, 255, 0, 255, 255, 255, 255]));
        Assert.IsTrue(ModelPreviewMaterials.HasTranslucentPixels([255, 255, 255, 128]));
    }

    [TestMethod]
    public void MaterialPassesPreserveAuthoredOrderingWithoutDrawingAnimatedSurfacesTwice()
    {
        var animation = new TextureAnimation(TextureAnimationMode.NativeItemTransform, NativeEffectSelector: 3);
        PreviewMaterialTexture? Resolve(string? name) => name switch
        {
            "stone" => new(TextureAnimation.None, TextureAnimation.None, TextureOverlayMode.None, false, false),
            "fire" => new(animation, TextureAnimation.None, TextureOverlayMode.None, false, true),
            "glow" => new(TextureAnimation.None, animation, TextureOverlayMode.MultiTextureFill, true, true),
            _ => null
        };
        var draws = ModelPreviewMaterials.Enumerate(Surfaces, null, "preview", Resolve, -1).ToArray();
        CollectionAssert.AreEqual(new[] { PreviewMaterialPass.Static, PreviewMaterialPass.AnimatedBase, PreviewMaterialPass.EffectOverlay }, draws.Select(draw => draw.Pass).ToArray());
        CollectionAssert.AreEqual(new[] { "stone", "fire", "glow" }, draws.Select(draw => draw.Surface.TextureName).ToArray());
        Assert.AreEqual(19f, draws[1].AnimationValue);
        Assert.IsTrue(draws[2].UsesOverlay);
    }

    [TestMethod]
    public void NativeSecondaryGlowUsesTheRendererBindingAndKeepsStaticSurfacesOpaque()
    {
        var animation = new TextureAnimation(TextureAnimationMode.NativeItemTransform, NativeEffectSelector: 1);
        var draws = ModelPreviewMaterials.Enumerate([Surfaces[0]], null, "preview", _ =>
            new(animation, TextureAnimation.None, TextureOverlayMode.None, false, false), 7).ToArray();
        Assert.AreEqual(1, draws.Length); Assert.AreEqual(7f, draws[0].Color.W);
        Assert.AreEqual(17f, draws[0].AnimationValue);
    }

    [TestMethod]
    public void AssetCameraFitsBothPortraitAndLandscapeAndKeepsGroundGeometryInWorldUnits()
    {
        foreach (var aspect in new[] { .5f, 1f, 2f })
        {
            var projection = PreviewCamera.ViewProjection(100, aspect, 1, true);
            foreach (var axis in new[] { Vector3.UnitX, -Vector3.UnitX, Vector3.UnitY, -Vector3.UnitY, Vector3.UnitZ, -Vector3.UnitZ })
            {
                var projected = Vector4.Transform(new Vector4(axis * 50, 1), projection);
                Assert.IsTrue(MathF.Abs(projected.X / projected.W) <= 1 && MathF.Abs(projected.Y / projected.W) <= 1);
                Assert.IsTrue(projected.Z / projected.W is >= 0 and <= 1);
            }
        }
    }
}
