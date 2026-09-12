using System;
using System.Collections.Generic;
using System.Numerics;
using Sacred.Granny.Meshes;
using Sacred.Particles;

namespace Sacred.Inventory.Effects;

internal sealed class EffectMeshBuilder
{
    private readonly List<VertexPositionNormalTexture> _vertices = [];
    private readonly List<ushort> _indices = [];
    private readonly List<EquipmentEffectSurface> _surfaces = [];
    private readonly List<string?> _vertexBoneNames = [];
    private readonly List<bool> _vertexDetachesAfterSpawn = [];
    private string? _attachmentBoneName;
    private readonly List<NativeModelEffectSimulation> _nativeEffects = [];

    public void AddNativeEffect(SacredModelEffectDefinition definition, Vector3 start, Vector3 end,
        string? boneName, Vector3 direction)
    {
        var count = definition.PointCount > 0 ? definition.PointCount :
            definition.Emission.EmissionInterval > 0 ?
                (int)MathF.Ceiling(255f / -definition.Motion.FadeChangeRate / definition.Emission.EmissionInterval) + 1 : 0;
        var firstVertex = _vertices.Count;
        var surfaces = new EquipmentEffectSurface[count];
        for (var index = 0; index < count; index++)
        {
            // Emitted particles fade independently; each chain layer shares one draw.
            var colored = definition.CornerColors.Count == 4;
            var color = colored ? new Vector4(1, 1, 1, NativeModelEffectSimulation.Unpack(definition.CornerColors[0]).W) : Vector4.Zero;
            AddBillboard(start, 0, 0, definition.TextureName, color,
                colored ? ParticleTextureMode.NativeModelColored : ParticleTextureMode.NativeModel, colored ? 0 : index);
            surfaces[index] = _surfaces[^1];
        }
        _nativeEffects.Add(new NativeModelEffectSimulation(definition, start, end, boneName,
            direction, firstVertex, surfaces));
        // These vertices are updated by the simulation, including detached particles.
        for (var index = firstVertex; index < _vertices.Count; index++)
            _vertexBoneNames[index] = null;
        if (definition.HaloCornerColors.Count == 4)
            AddNativeEffect(definition with { HalfSize = definition.HaloHalfSize,
                CornerColors = definition.HaloCornerColors, HaloCornerColors = [] }, start, end, boneName, direction);
    }

    public void BeginAttachment(string? attachmentBoneName) =>
        _attachmentBoneName = attachmentBoneName;

    public void AddNativeBeam(SacredModelEffectDefinition definition, Vector3 start, Vector3 end)
    {
        AddField(definition.HalfSize, definition.Color, definition.BeamDensity);
        if (definition.HaloDensity > 0)
            AddField(definition.HaloHalfSize, definition.HaloColor, definition.HaloDensity);

        void AddField(float halfSize, uint color, float density)
        {
            // 0x40DA80: floor(length * density) camera-facing glow quads; end is exclusive.
            var count = Math.Max(1, (int)(Vector3.Distance(start, end) * density));
            for (var i = 0; i < count; i++)
                AddBillboard(Vector3.Lerp(start, end, (float)i / count), halfSize * 2, halfSize * 2,
                    definition.TextureName, NativeModelEffectSimulation.Unpack(color), ParticleTextureMode.NativeModel);
        }
    }

    public void AddNativeGlowLine(Vector3 start, Vector3 end, string textureName,
        uint color, float halfSize, float density)
    {
        // renderGlowLine 0x40DA80: max(1, floor(length * density)) camera-facing
        // quads, with the end point excluded.
        var count = Math.Max(1, (int)(Vector3.Distance(start, end) * density));
        for (var i = 0; i < count; i++)
            AddBillboard(Vector3.Lerp(start, end, (float)i / count), halfSize * 2, halfSize * 2,
                textureName, NativeModelEffectSimulation.Unpack(color), ParticleTextureMode.NativeModel);
    }

    public void AddBillboard(
        Vector3 center,
        float width,
        float height,
        string textureName,
        Vector4 color,
        ParticleTextureMode textureMode,
        float phase = 0.0f,
        bool bindToAttachment = true,
        string? boneName = null)
    {
        var halfWidth = width * 0.5f;
        var halfHeight = height * 0.5f;
        var usesPerParticlePhase = textureMode is
            ParticleTextureMode.FirePop or
            ParticleTextureMode.PoisonStatic;
        var vertexMarker = 1.0f + (usesPerParticlePhase ? phase : 0.0f);
        var surfacePhase = usesPerParticlePhase ? 0.0f : phase;
        AddSurface(textureName, color, textureMode, surfacePhase, () =>
        {
            EnsureVertexCapacity();
            var start = (ushort)_vertices.Count;
            AddVertex(center, new Vector3(-halfWidth, -halfHeight, vertexMarker), new Vector2(0.0f, 1.0f), bindToAttachment, boneName);
            AddVertex(center, new Vector3( halfWidth, -halfHeight, vertexMarker), new Vector2(1.0f, 1.0f), bindToAttachment, boneName);
            AddVertex(center, new Vector3( halfWidth,  halfHeight, vertexMarker), new Vector2(1.0f, 0.0f), bindToAttachment, boneName);
            AddVertex(center, new Vector3(-halfWidth,  halfHeight, vertexMarker), new Vector2(0.0f, 0.0f), bindToAttachment, boneName);
            if (textureMode == ParticleTextureMode.NativeModelColored)
            {
                // Native D3DPT_TRIANGLESTRIP: BL, TL, BR, TR. Its TL--BR diagonal
                // interpolates the authored blue/green center; BL--TR turns it purple.
                _indices.Add(start);
                _indices.Add((ushort)(start + 3));
                _indices.Add((ushort)(start + 1));
                _indices.Add((ushort)(start + 1));
                _indices.Add((ushort)(start + 3));
                _indices.Add((ushort)(start + 2));
            }
            else
                AddQuadIndices(start);
        });
    }

    public void AddCrossedStrip(
        Vector3 start,
        Vector3 end,
        float width,
        string textureName,
        Vector4 color,
        ParticleTextureMode textureMode)
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

        AddSurface(textureName, color, textureMode, 0.0f, () =>
        {
            AddQuad(start - firstAxis, start + firstAxis, end + firstAxis, end - firstAxis);
            AddQuad(start - secondAxis, start + secondAxis, end + secondAxis, end - secondAxis);
        });
    }

    public EquipmentEffectScene? Build()
    {
        if (_indices.Count == 0)
            return null;

        var vertices = _vertices.ToArray();
        return new EquipmentEffectScene(
            new Mesh(vertices, _indices.ToArray()),
            _surfaces.ToArray(),
            Array.ConvertAll(vertices, static vertex => vertex.Position),
            _vertexBoneNames.ToArray(),
            _vertexDetachesAfterSpawn.ToArray()) { NativeEffects = _nativeEffects.ToArray() };
    }

    private void AddSurface(
        string textureName,
        Vector4 color,
        ParticleTextureMode textureMode,
        float phase,
        Action addGeometry)
    {
        var start = _indices.Count;
        addGeometry();
        var indexCount = _indices.Count - start;
        if (indexCount == 0)
            return;

        var previous = _surfaces.Count > 0 ? _surfaces[^1] : null;
        if (previous is not null &&
            previous.IndexStart + previous.IndexCount == start &&
            previous.TextureName.Equals(textureName, StringComparison.OrdinalIgnoreCase) &&
            previous.Color == color &&
            previous.TextureMode == textureMode &&
            previous.Phase == phase)
        {
            previous.Extend(indexCount);
            return;
        }

        _surfaces.Add(new EquipmentEffectSurface(
            start,
            indexCount,
            textureName,
            color,
            textureMode,
            phase));
    }

    private void AddQuad(Vector3 bottomLeft, Vector3 bottomRight, Vector3 topRight, Vector3 topLeft)
    {
        EnsureVertexCapacity();
        var start = (ushort)_vertices.Count;
        AddVertex(bottomLeft, Vector3.Zero, new Vector2(0.0f, 1.0f));
        AddVertex(bottomRight, Vector3.Zero, new Vector2(1.0f, 1.0f));
        AddVertex(topRight, Vector3.Zero, new Vector2(1.0f, 0.0f));
        AddVertex(topLeft, Vector3.Zero, new Vector2(0.0f, 0.0f));
        AddQuadIndices(start);
    }

    private void AddVertex(
        Vector3 position,
        Vector3 normal,
        Vector2 textureCoordinate,
        bool bindToAttachment = true,
        string? boneName = null)
    {
        _vertices.Add(new VertexPositionNormalTexture(position, normal, textureCoordinate));
        var binding = boneName ?? _attachmentBoneName;
        _vertexBoneNames.Add(binding);
        _vertexDetachesAfterSpawn.Add(!bindToAttachment && binding is not null);
    }

    private void EnsureVertexCapacity()
    {
        if (_vertices.Count > ushort.MaxValue - 4)
            throw new InvalidOperationException("Equipment effect mesh is too large for 16-bit indices.");
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
