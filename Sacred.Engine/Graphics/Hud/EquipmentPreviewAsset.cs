using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Sacred.Assets.Paks.Texture;
using Sacred.Core.Pak.Weapon;
using Sacred.Engine.Assets;
using Sacred.Granny.Meshes;
using Sacred.UI.Inventory;

namespace Sacred.Engine.Graphics.Hud;

/// <summary>Authored model placement and resolved materials, ready for a transparent slot layer.</summary>
internal sealed record EquipmentPreviewAsset(SacredEquipment Equipment, Mesh Mesh, Matrix4x4 Transform,
    Vector2 Minimum, Vector2 Size, IReadOnlyDictionary<string, ModelTextureReference> Materials,
    IReadOnlyDictionary<string, TextureAsset> Textures)
{
    public static async Task<EquipmentPreviewAsset?> LoadAsync(AssetManager assets, SacredEquipment equipment)
    {
        try
        {
            var model = await assets.LoadEquipmentPreviewAsync(equipment).ConfigureAwait(false);
            if (model.Model.Mesh is not { } mesh) return null;
            // Demo getModel reflects Y before Weapon.pak scale/Rx/Ry/native Rz.
            var transform = Matrix4x4.CreateTranslation(model.Model.Diagnostics?.SourceOriginOffset ?? Vector3.Zero) *
                Matrix4x4.CreateScale(1, -1, 1);
            if (equipment.PreviewScale != 0)
                transform *= Matrix4x4.CreateScale(equipment.PreviewScale) *
                    Matrix4x4.CreateRotationX(equipment.PreviewRotation.X) *
                    Matrix4x4.CreateRotationY(equipment.PreviewRotation.Y) *
                    Matrix4x4.CreateRotationZ(-equipment.PreviewRotation.Z) *
                    Matrix4x4.CreateTranslation(equipment.PreviewOffset.X, 0, equipment.PreviewOffset.Z);
            var minimum = Vector2.Zero;
            var maximum = Vector2.Zero;
            foreach (var vertex in mesh.Vertices)
            {
                var p = Vector3.Transform(vertex.Position, transform);
                minimum = Vector2.Min(minimum, new(p.X, -p.Z));
                maximum = Vector2.Max(maximum, new(p.X, -p.Z));
            }
            // One transparent pixel on each side protects filtered edges in the atlas.
            minimum = new(MathF.Floor(minimum.X) - 1, MathF.Floor(minimum.Y) - 1);
            maximum = new(MathF.Ceiling(maximum.X) + 1, MathF.Ceiling(maximum.Y) + 1);
            var materials = mesh.Surfaces.Where(s => !string.IsNullOrWhiteSpace(s.TextureName))
                .Select(s => s.TextureName!).Distinct(StringComparer.OrdinalIgnoreCase)
                .ToDictionary(n => n, n => model.TextureAliases.GetValueOrDefault(n, ModelTextureReference.Static(n)),
                    StringComparer.OrdinalIgnoreCase);
            var names = materials.Values.SelectMany(m => m.HasOverlay
                ? new[] { m.TextureName, m.OverlayTextureName! } : new[] { m.TextureName })
                .Concat(materials.Values.Any(m => m.Animation.HasSecondaryGlow || m.OverlayAnimation.HasSecondaryGlow)
                    ? new[] { TextureAnimation.MultitextureGlowName } : Array.Empty<string>())
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var textures = await Task.WhenAll(names.Select(async n =>
                (Name: n, Texture: await assets.LoadModelTextureAsync(n).ConfigureAwait(false)))).ConfigureAwait(false);
            EngineLog.WriteLine($"Equipment UI model loaded: {equipment.IdemId}; authored preview {maximum - minimum}.");
            return new(equipment, mesh, transform, minimum, maximum - minimum, materials,
                textures.ToDictionary(t => t.Name, t => t.Texture, StringComparer.OrdinalIgnoreCase));
        }
        catch (Exception error)
        {
            EngineLog.WriteLine($"Equipment UI model unavailable: {equipment.IdemId}; {error.Message}");
            return null;
        }
    }

    public static Vector2 Anchor(SacredEquipment item, bool hand) => hand
        ? WeaponSlotLayout.ItemCenter(item) : new(item.Width * 16, item.Height * 16);
}
