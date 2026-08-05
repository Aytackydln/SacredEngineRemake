using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Sacred.Granny;

namespace Sacred.Engine.Rendering.EquipmentEffects;

public sealed class EquipmentEffectScene
{
    private readonly Vector3[] _bindPositions;
    private readonly string?[] _vertexBoneNames;

    internal EquipmentEffectScene(
        Mesh mesh,
        EquipmentEffectSurface[] surfaces,
        Vector3[] bindPositions,
        string?[] vertexBoneNames)
    {
        Mesh = mesh;
        Surfaces = surfaces;
        _bindPositions = bindPositions;
        _vertexBoneNames = vertexBoneNames;
    }

    public Mesh Mesh { get; }
    public IReadOnlyList<EquipmentEffectSurface> Surfaces { get; }

    public IReadOnlyList<string> TextureNames => Surfaces
        .Select(static surface => surface.TextureName)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    public void ApplyPose(GrnAnimatedMesh animatedMesh)
    {
        var changed = false;
        for (var index = 0; index < _bindPositions.Length; index++)
        {
            var boneName = _vertexBoneNames[index];
            if (boneName is null ||
                !animatedMesh.TryTransformRigidPoint(boneName, _bindPositions[index], out var position))
            {
                continue;
            }

            Mesh.Vertices[index] = Mesh.Vertices[index] with { Position = position };
            changed = true;
        }

        if (changed)
            Mesh.NotifyVerticesChanged();
    }
}

public sealed class EquipmentEffectSurface(
    int indexStart,
    int indexCount,
    string textureName,
    Vector4 color,
    EquipmentEffectTextureMode textureMode,
    float phase)
{
    public int IndexStart { get; } = indexStart;
    public int IndexCount { get; } = indexCount;
    public string TextureName { get; } = textureName;
    public Vector4 Color { get; } = color;
    public EquipmentEffectTextureMode TextureMode { get; } = textureMode;
    public float Phase { get; } = phase;
}

public enum EquipmentEffectTextureMode
{
    Luminance = 1,
    Atlas4X4 = 2,
    Alpha = 3,
    BouncyAlpha = 4,
    MagicOrb = 5,
    FirePop = 6,
    PoisonStatic = 7,
    WeaponGlowFlare = 8
}
