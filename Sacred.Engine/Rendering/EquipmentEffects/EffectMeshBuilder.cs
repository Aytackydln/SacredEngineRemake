using System;
using System.Collections.Generic;
using System.Numerics;
using Sacred.Granny;

namespace Sacred.Engine.Rendering.EquipmentEffects;

internal sealed class EffectMeshBuilder
{
    private readonly List<VertexPositionNormalTexture> _vertices = [];
    private readonly List<ushort> _indices = [];
    private readonly List<EquipmentEffectSurface> _surfaces = [];
    private readonly List<string?> _vertexBoneNames = [];
    private string? _attachmentBoneName;

    public void BeginAttachment(string? attachmentBoneName) =>
        _attachmentBoneName = attachmentBoneName;

    public void AddBillboard(
        Vector3 center,
        float width,
        float height,
        string textureName,
        Vector4 color,
        EquipmentEffectTextureMode textureMode,
        float phase = 0.0f)
    {
        var halfWidth = width * 0.5f;
        var halfHeight = height * 0.5f;
        AddSurface(textureName, color, textureMode, phase, () =>
        {
            EnsureVertexCapacity();
            var start = (ushort)_vertices.Count;
            AddVertex(center, new Vector3(-halfWidth, -halfHeight, 1.0f), new Vector2(0.0f, 1.0f));
            AddVertex(center, new Vector3( halfWidth, -halfHeight, 1.0f), new Vector2(1.0f, 1.0f));
            AddVertex(center, new Vector3( halfWidth,  halfHeight, 1.0f), new Vector2(1.0f, 0.0f));
            AddVertex(center, new Vector3(-halfWidth,  halfHeight, 1.0f), new Vector2(0.0f, 0.0f));
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
            _vertexBoneNames.ToArray());
    }

    private void AddSurface(
        string textureName,
        Vector4 color,
        EquipmentEffectTextureMode textureMode,
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

    private void AddVertex(Vector3 position, Vector3 normal, Vector2 textureCoordinate)
    {
        _vertices.Add(new VertexPositionNormalTexture(position, normal, textureCoordinate));
        _vertexBoneNames.Add(_attachmentBoneName);
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
