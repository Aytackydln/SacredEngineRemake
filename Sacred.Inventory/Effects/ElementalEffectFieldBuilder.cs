using System;
using System.Collections.Generic;
using System.Numerics;
using Sacred.Granny;

namespace Sacred.Inventory.Effects;

internal static class ElementalEffectFieldBuilder
{
    // Sacred.exe's Granny effect table loads this sprite specifically for its
    // hard-coded model glows. Its alpha channel is the soft circular falloff
    // visible around elemental weapons in the original renderer.
    private const string ElementGlowTexture = "PARTICLE_GLOW01.TGA";

    private static readonly IReadOnlyDictionary<ElementKind, ElementalEffectFieldParameters> ParametersByKind =
        new Dictionary<ElementKind, ElementalEffectFieldParameters>
        {
            [ElementKind.Fire] = new(
                IsUniform: false,
                SpreadScale: 1.42f,
                MinimumSizeScale: 1.2f,
                MaximumSizeScale: 2.65f,
                CountSpacingScale: 1.45f,
                CountOffset: 0,
                MinimumCount: 55,
                MaximumCount: 160,
                CollapsedCount: 4,
                TextureMode: EquipmentEffectTextureMode.FirePop),
            [ElementKind.Magic] = new(
                IsUniform: true,
                SpreadScale: 0.12f,
                MinimumSizeScale: 3.4f,
                MaximumSizeScale: 3.4f,
                CountSpacingScale: 2.4f * 0.58f,
                CountOffset: 1,
                MinimumCount: null,
                MaximumCount: null,
                CollapsedCount: 1,
                TextureMode: EquipmentEffectTextureMode.MagicOrb),
            [ElementKind.Poison] = new(
                IsUniform: false,
                SpreadScale: 1.85f,
                MinimumSizeScale: 1.15f,
                MaximumSizeScale: 1.65f,
                CountSpacingScale: 1.35f,
                CountOffset: 0,
                MinimumCount: 280,
                MaximumCount: 280,
                CollapsedCount: 4,
                TextureMode: EquipmentEffectTextureMode.PoisonStatic)
        };

    public static void Add(
        EffectMeshBuilder builder,
        Vector3 start,
        Vector3 end,
        ElementEffect element,
        float unit,
        IReadOnlyList<GrnSurfaceTriangleDiagnostics> surfaceTriangles)
    {
        if (!ParametersByKind.TryGetValue(element.Kind, out var parameters))
            throw new ArgumentOutOfRangeException(nameof(element), element.Kind, "Unsupported element kind.");

        if (parameters.IsUniform)
        {
            AddUniformField(builder, start, end, element, unit, parameters);
            return;
        }

        var selectedSurface = ElementalEffectSurfaceSelector.Select(
            surfaceTriangles,
            start,
            end);
        if (!selectedSurface.IsEmpty)
        {
            AddSurfaceField(builder, start, end, element, unit, parameters, selectedSurface);
            return;
        }

        AddScatteredField(builder, start, end, element, unit, parameters);
    }

    private static void AddSurfaceField(
        EffectMeshBuilder builder,
        Vector3 start,
        Vector3 end,
        ElementEffect element,
        float unit,
        ElementalEffectFieldParameters parameters,
        ElementalEffectSurface surface)
    {
        var distance = Vector3.Distance(start, end);
        var surfacePoints = surface.CreateUniformSamples(CalculateCount(distance, unit, parameters));
        var random = new StableEffectRandom(CreateSeed(start, end, element.Kind));
        for (var index = 0; index < surfacePoints.Length; index++)
        {
            var diameter = unit * Lerp(
                parameters.MinimumSizeScale,
                parameters.MaximumSizeScale,
                random.NextFloat());
            var phase = parameters.TextureMode is
                EquipmentEffectTextureMode.FirePop or
                EquipmentEffectTextureMode.PoisonStatic
                ? random.NextFloat()
                : 0.0f;
            builder.AddBillboard(
                surfacePoints[index],
                diameter,
                diameter,
                ElementGlowTexture,
                element.Color,
                parameters.TextureMode,
                phase);
        }
    }

    private static void AddUniformField(
        EffectMeshBuilder builder,
        Vector3 start,
        Vector3 end,
        ElementEffect element,
        float unit,
        ElementalEffectFieldParameters parameters)
    {
        var span = end - start;
        var distance = span.Length();
        var diameter = unit * parameters.MinimumSizeScale;
        var count = Math.Max(1, checked((int)MathF.Ceiling(distance)));

        for (var index = 0; index < count; index++)
        {
            var progress = count == 1 ? 0.0f : index / (float)(count - 1);
            builder.AddBillboard(
                Vector3.Lerp(start, end, progress),
                diameter,
                diameter,
                ElementGlowTexture,
                element.Color,
                parameters.TextureMode);
        }
    }

    private static void AddScatteredField(
        EffectMeshBuilder builder,
        Vector3 start,
        Vector3 end,
        ElementEffect element,
        float unit,
        ElementalEffectFieldParameters parameters)
    {
        var span = end - start;
        var distance = span.Length();
        var axis = distance > 0.001f ? span / distance : Vector3.UnitZ;
        var firstRadiusAxis = CreatePerpendicular(axis);
        var secondRadiusAxis = Vector3.Normalize(Vector3.Cross(axis, firstRadiusAxis));
        var cylinderRadius = unit * parameters.SpreadScale;
        var count = CalculateCount(distance, unit, parameters);
        var random = new StableEffectRandom(CreateSeed(start, end, element.Kind));

        for (var index = 0; index < count; index++)
        {
            var along = distance < 0.001f ? 0.0f : random.NextFloat();
            var angle = random.NextFloat() * MathF.Tau;
            var radius = MathF.Sqrt(random.NextFloat()) * cylinderRadius;
            var radialOffset =
                firstRadiusAxis * (MathF.Cos(angle) * radius) +
                secondRadiusAxis * (MathF.Sin(angle) * radius);
            var center = start + axis * (distance * along) + radialOffset;
            var diameter = unit * Lerp(
                parameters.MinimumSizeScale,
                parameters.MaximumSizeScale,
                random.NextFloat());

            builder.AddBillboard(
                center,
                diameter,
                diameter,
                ElementGlowTexture,
                element.Color,
                parameters.TextureMode,
                random.NextFloat());
        }
    }

    private static int CalculateCount(
        float distance,
        float unit,
        ElementalEffectFieldParameters parameters)
    {
        if (distance < 0.001f)
            return parameters.CollapsedCount;

        var count = Math.Max(
            1,
            (int)MathF.Ceiling(distance / (unit * parameters.CountSpacingScale)) + parameters.CountOffset);
        return parameters.MinimumCount is { } minimum && parameters.MaximumCount is { } maximum
            ? Math.Clamp(count, minimum, maximum)
            : count;
    }

    private static Vector3 CreatePerpendicular(Vector3 axis)
    {
        var reference = MathF.Abs(Vector3.Dot(axis, Vector3.UnitZ)) < 0.9f
            ? Vector3.UnitZ
            : Vector3.UnitX;
        return Vector3.Normalize(Vector3.Cross(axis, reference));
    }

    private static uint CreateSeed(Vector3 start, Vector3 end, ElementKind kind)
    {
        var seed = 2166136261u;
        Mix(ref seed, BitConverter.SingleToUInt32Bits(start.X));
        Mix(ref seed, BitConverter.SingleToUInt32Bits(start.Y));
        Mix(ref seed, BitConverter.SingleToUInt32Bits(start.Z));
        Mix(ref seed, BitConverter.SingleToUInt32Bits(end.X));
        Mix(ref seed, BitConverter.SingleToUInt32Bits(end.Y));
        Mix(ref seed, BitConverter.SingleToUInt32Bits(end.Z));
        Mix(ref seed, (uint)kind);
        return seed == 0 ? 0x9E3779B9u : seed;
    }

    private static void Mix(ref uint seed, uint value)
    {
        seed ^= value;
        seed *= 16777619u;
    }

    private static float Lerp(float start, float end, float amount) =>
        start + (end - start) * amount;

    private struct StableEffectRandom(uint state)
    {
        private uint _state = state;

        public float NextFloat()
        {
            _state ^= _state << 13;
            _state ^= _state >> 17;
            _state ^= _state << 5;
            return (_state >> 8) * (1.0f / 16777216.0f);
        }
    }

    private sealed record ElementalEffectFieldParameters(
        bool IsUniform,
        float SpreadScale,
        float MinimumSizeScale,
        float MaximumSizeScale,
        float CountSpacingScale,
        int CountOffset,
        int? MinimumCount,
        int? MaximumCount,
        int CollapsedCount,
        EquipmentEffectTextureMode TextureMode);
}

internal enum ElementKind
{
    Fire,
    Magic,
    Poison
}

internal readonly record struct ElementEffect(ElementKind Kind, Vector4 Color);
