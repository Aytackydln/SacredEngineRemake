using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Sacred.Core.Pak.Weapon;
using Sacred.Granny;

namespace Sacred.ItemViewer.Avalonia.ItemViewer;

public sealed record EquipmentEffectScene(Mesh? Mesh, IReadOnlyList<EquipmentEffectSurface> Surfaces)
{
    public static EquipmentEffectScene Empty { get; } = new(null, []);

    public IReadOnlyList<string> TextureNames => Surfaces
        .Select(static surface => surface.TextureName)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();
}

public readonly record struct EquipmentEffectSurface(
    int IndexStart,
    int IndexCount,
    string TextureName,
    Vector4 Color,
    EquipmentEffectTextureMode TextureMode,
    Vector3 MotionVector,
    float Phase);

public enum EquipmentEffectTextureMode
{
    Luminance = 1,
    Atlas4X4 = 2,
    Alpha = 3,
    BouncyAlpha = 4,
    MagicOrb = 5,
    FirePop = 6,
    PoisonFlow = 7
}

internal static class EquipmentEffectSceneFactory
{
    private const string FireTexture = "PARTICLE_FIRE02.TGA";
    private const string OrbTexture = "PARTICLE_GLOW03.TGA";
    private const string LineTexture = "PARTICLE_LINE01.TGA";
    private const string BouncyLineTexture = "FX_STREAKS01.TGA";

    public static EquipmentEffectScene Create(
        GrnAsset asset,
        SacredEquipmentDamage damage)
    {
        var diagnostics = asset.Diagnostics;
        if (diagnostics is null)
            return EquipmentEffectScene.Empty;

        var points = diagnostics.Slices
            .SelectMany(static slice => slice.Bones)
            .Select(static bone => TryCreatePoint(bone, out var point) ? point : (EffectAnchorPoint?)null)
            .Where(static point => point.HasValue)
            .Select(static point => point!.Value)
            .GroupBy(static point => point.Anchor.BoneName, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .ToArray();
        if (points.Length == 0)
            return EquipmentEffectScene.Empty;

        var boundsSize = diagnostics.WholeModelBounds is { } bounds
            ? Vector3.Distance(bounds.Min, bounds.Max)
            : 40.0f;
        var unit = Math.Clamp(boundsSize * 0.035f, 1.5f, 5.0f);
        var builder = new EffectMeshBuilder();
        var elements = GetElements(damage).ToArray();
        var emitters = OfKind(points, SacredEquipmentEffectAnchorKind.ElementalEmitter);
        var primaryEmitter = emitters.FirstOrDefault();
        var isTorch = asset.Name.StartsWith("TORCH_", StringComparison.OrdinalIgnoreCase);

        if (isTorch && damage.Fire.IsPresent && primaryEmitter != default)
        {
            var flameWidth = Math.Max(16.0f, boundsSize * 0.65f);
            var flameHeight = Math.Max(25.0f, boundsSize * 0.95f);
            builder.AddBillboard(
                primaryEmitter.Position + Vector3.UnitZ * (flameHeight * 0.32f),
                flameWidth,
                flameHeight,
                FireTexture,
                new Vector4(1.0f, 0.72f, 0.42f, 0.94f),
                EquipmentEffectTextureMode.Atlas4X4);
        }
        else if (primaryEmitter != default)
        {
            foreach (var element in elements)
                AddElementParticleField(builder, emitters, element, unit);
        }

        var dominantColor = elements.FirstOrDefault().Color;
        if (dominantColor == default)
            dominantColor = new Vector4(0.45f, 0.72f, 1.0f, 0.82f);

        var standardEffects = OfKind(points, SacredEquipmentEffectAnchorKind.StandardEffect);
        var streaks = OfKind(points, SacredEquipmentEffectAnchorKind.Streak);
        if (streaks.Length == 0)
        {
            foreach (var standard in standardEffects)
                builder.AddBillboard(
                    standard.Position,
                    unit * 2.7f,
                    unit * 2.7f,
                    OrbTexture,
                    new Vector4(0.42f, 0.66f, 1.0f, 0.78f),
                    EquipmentEffectTextureMode.Luminance);
        }

        // The standard-effect attachment takes precedence on models which also carry a generic
        // weapon_gl bone (for example Seraphim_LongSword). Multiple weapon_gl anchors describe
        // one terminal glow plus the short trail leading into it.
        if (standardEffects.Length == 0)
        {
            var glows = OfKind(points, SacredEquipmentEffectAnchorKind.Glow);
            if (glows.Length > 0)
                builder.AddBillboard(
                    glows[0].Position,
                    unit * 3.0f,
                    unit * 3.0f,
                    OrbTexture,
                    dominantColor,
                    EquipmentEffectTextureMode.Luminance);
            for (var index = 1; index < glows.Length; index++)
                builder.AddCrossedStrip(
                    glows[index - 1].Position,
                    glows[index].Position,
                    unit * 0.42f,
                    LineTexture,
                    dominantColor,
                    EquipmentEffectTextureMode.Alpha);
        }

        var streakColor = new Vector4(0.58f, 0.78f, 1.0f, 0.72f);
        var streakOrigin = standardEffects.FirstOrDefault();
        if (streakOrigin != default)
        {
            foreach (var streak in streaks)
            {
                var away = streak.Position - streakOrigin.Position;
                if (away.LengthSquared() < 0.0001f)
                    continue;
                var end = streak.Position + Vector3.Normalize(away) * (unit * 5.0f);
                builder.AddCrossedStrip(
                    streak.Position,
                    end,
                    unit * 0.62f,
                    BouncyLineTexture,
                    streakColor,
                    EquipmentEffectTextureMode.BouncyAlpha);
            }
        }

        AddModelEmitterStreak(asset, primaryEmitter, unit, dominantColor, builder);

        return builder.Build();
    }

    private static void AddModelEmitterStreak(
        GrnAsset asset,
        EffectAnchorPoint primaryEmitter,
        float unit,
        Vector4 color,
        EffectMeshBuilder builder)
    {
        if (primaryEmitter == default)
            return;

        var cylinder = asset.Diagnostics?.Slices
            .SelectMany(static slice => slice.Bones)
            .FirstOrDefault(static bone => bone.Name.Equals("Cylinder01", StringComparison.OrdinalIgnoreCase));
        if (cylinder is null)
            return;

        var away = primaryEmitter.Position - cylinder.Position;
        if (away.LengthSquared() < 0.0001f)
            away = Vector3.UnitZ;
        var end = primaryEmitter.Position + Vector3.Normalize(away) * (unit * 6.0f);
        builder.AddCrossedStrip(
            primaryEmitter.Position,
            end,
            unit * 0.68f,
            BouncyLineTexture,
            color,
            EquipmentEffectTextureMode.BouncyAlpha);
    }

    private static void AddElementParticleField(
        EffectMeshBuilder builder,
        IReadOnlyList<EffectAnchorPoint> emitters,
        ElementEffect element,
        float unit)
    {
        var start = emitters[0].Position;
        var end = emitters.Count > 1 ? emitters[1].Position : start;
        var span = end - start;
        var distance = span.Length();
        var count = distance < 0.001f
            ? 1
            : Math.Clamp((int)MathF.Ceiling(distance / Math.Max(unit * 2.4f, 1.0f)), 3, 12);
        var diameter = unit * (element.Kind == ElementKind.Magic ? 1.55f : 1.35f);

        for (var index = 0; index < count; index++)
        {
            var phase = count == 1 ? 0.0f : index / (float)count;
            var position = element.Kind == ElementKind.Poison
                ? start
                : Vector3.Lerp(start, end, count == 1 ? 0.0f : index / (float)(count - 1));
            builder.AddBillboard(
                position,
                diameter,
                diameter,
                OrbTexture,
                element.Color,
                element.Kind switch
                {
                    ElementKind.Magic => EquipmentEffectTextureMode.MagicOrb,
                    ElementKind.Fire => EquipmentEffectTextureMode.FirePop,
                    _ => EquipmentEffectTextureMode.PoisonFlow
                },
                element.Kind == ElementKind.Poison ? span : Vector3.Zero,
                phase);
        }
    }

    private static EffectAnchorPoint[] OfKind(
        IEnumerable<EffectAnchorPoint> points,
        SacredEquipmentEffectAnchorKind kind) =>
        points.Where(point => point.Anchor.Kind == kind)
            .OrderBy(static point => point.Anchor.Index)
            .ToArray();

    private static IEnumerable<ElementEffect> GetElements(SacredEquipmentDamage damage)
    {
        if (damage.Fire.IsPresent)
            yield return new ElementEffect(ElementKind.Fire, new Vector4(1.0f, 0.533f, 0.267f, 0.251f));
        if (damage.Magic.IsPresent)
            yield return new ElementEffect(ElementKind.Magic, new Vector4(0.439f, 0.20f, 1.0f, 0.314f));
        if (damage.Poison.IsPresent)
            yield return new ElementEffect(ElementKind.Poison, new Vector4(0.267f, 1.0f, 0.267f, 0.251f));
    }

    private static bool TryCreatePoint(GrnBoneDiagnostics bone, out EffectAnchorPoint point)
    {
        if (SacredEquipmentEffectAnchor.TryParse(bone.Name, out var anchor))
        {
            point = new EffectAnchorPoint(anchor, bone.Position);
            return true;
        }

        point = default;
        return false;
    }

    private enum ElementKind { Fire, Magic, Poison }

    private readonly record struct ElementEffect(ElementKind Kind, Vector4 Color);
    private readonly record struct EffectAnchorPoint(SacredEquipmentEffectAnchor Anchor, Vector3 Position);

    private sealed class EffectMeshBuilder
    {
        private readonly List<VertexPositionNormalTexture> _vertices = [];
        private readonly List<ushort> _indices = [];
        private readonly List<EquipmentEffectSurface> _surfaces = [];

        public void AddBillboard(
            Vector3 center,
            float width,
            float height,
            string textureName,
            Vector4 color,
            EquipmentEffectTextureMode textureMode,
            Vector3 motionVector = default,
            float phase = 0.0f)
        {
            var halfWidth = width * 0.5f;
            var halfHeight = height * 0.5f;
            AddSurface(textureName, color, textureMode, motionVector, phase, () =>
            {
                if (_vertices.Count > ushort.MaxValue - 4)
                    throw new InvalidOperationException("Equipment effect mesh is too large for 16-bit indices.");

                var start = (ushort)_vertices.Count;
                _vertices.Add(new VertexPositionNormalTexture(center, new Vector3(-halfWidth, -halfHeight, 1.0f), new Vector2(0.0f, 1.0f)));
                _vertices.Add(new VertexPositionNormalTexture(center, new Vector3(halfWidth, -halfHeight, 1.0f), new Vector2(1.0f, 1.0f)));
                _vertices.Add(new VertexPositionNormalTexture(center, new Vector3(halfWidth, halfHeight, 1.0f), new Vector2(1.0f, 0.0f)));
                _vertices.Add(new VertexPositionNormalTexture(center, new Vector3(-halfWidth, halfHeight, 1.0f), new Vector2(0.0f, 0.0f)));
                AddQuadIndices(start);
            });
        }

        public void AddCrossedStrip(
            Vector3 start,
            Vector3 end,
            float width,
            string textureName,
            Vector4 color,
            EquipmentEffectTextureMode textureMode)
        {
            var direction = end - start;
            if (direction.LengthSquared() < 0.0001f)
                return;

            direction = Vector3.Normalize(direction);
            var firstAxis = Vector3.Cross(direction, Vector3.UnitZ);
            if (firstAxis.LengthSquared() < 0.001f)
                firstAxis = Vector3.Cross(direction, Vector3.UnitX);
            firstAxis = Vector3.Normalize(firstAxis) * (width * 0.5f);
            var secondAxis = Vector3.Normalize(Vector3.Cross(direction, firstAxis)) * (width * 0.5f);

            AddSurface(textureName, color, textureMode, Vector3.Zero, 0.0f, () =>
            {
                AddQuad(start - firstAxis, start + firstAxis, end + firstAxis, end - firstAxis);
                AddQuad(start - secondAxis, start + secondAxis, end + secondAxis, end - secondAxis);
            });
        }

        public EquipmentEffectScene Build()
        {
            if (_indices.Count == 0)
                return EquipmentEffectScene.Empty;

            return new EquipmentEffectScene(new Mesh(_vertices.ToArray(), _indices.ToArray()), _surfaces.ToArray());
        }

        private void AddSurface(
            string textureName,
            Vector4 color,
            EquipmentEffectTextureMode textureMode,
            Vector3 motionVector,
            float phase,
            Action addGeometry)
        {
            var start = _indices.Count;
            addGeometry();
            _surfaces.Add(new EquipmentEffectSurface(
                start,
                _indices.Count - start,
                textureName,
                color,
                textureMode,
                motionVector,
                phase));
        }

        private void AddQuad(Vector3 bottomLeft, Vector3 bottomRight, Vector3 topRight, Vector3 topLeft)
        {
            if (_vertices.Count > ushort.MaxValue - 4)
                throw new InvalidOperationException("Equipment effect mesh is too large for 16-bit indices.");

            var start = (ushort)_vertices.Count;
            var normal = Vector3.Zero;
            _vertices.Add(new VertexPositionNormalTexture(bottomLeft, normal, new Vector2(0.0f, 1.0f)));
            _vertices.Add(new VertexPositionNormalTexture(bottomRight, normal, new Vector2(1.0f, 1.0f)));
            _vertices.Add(new VertexPositionNormalTexture(topRight, normal, new Vector2(1.0f, 0.0f)));
            _vertices.Add(new VertexPositionNormalTexture(topLeft, normal, new Vector2(0.0f, 0.0f)));
            AddQuadIndices(start);
        }

        private void AddQuadIndices(ushort start)
        {
            _indices.Add(start);
            _indices.Add((ushort)(start + 1));
            _indices.Add((ushort)(start + 2));
            _indices.Add(start);
            _indices.Add((ushort)(start + 2));
            _indices.Add((ushort)(start + 3));
        }
    }
}
