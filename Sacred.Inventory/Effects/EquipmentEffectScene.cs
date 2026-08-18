using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Sacred.Granny;
using Sacred.Granny.Animation;
using Sacred.Granny.Meshes;
using Sacred.Particles;

namespace Sacred.Inventory.Effects;

public sealed class EquipmentEffectScene
{
    private const float EmittedParticleLifetimeSeconds = 0.25f;

    private readonly Vector3[] _bindPositions;
    private readonly string?[] _vertexBoneNames;
    private readonly bool[] _vertexDetachesAfterSpawn;
    private readonly int[] _particleSpawnCycles;
    private float _particleElapsedSeconds;

    internal EquipmentEffectScene(
        Mesh mesh,
        EquipmentEffectSurface[] surfaces,
        Vector3[] bindPositions,
        string?[] vertexBoneNames,
        bool[] vertexDetachesAfterSpawn)
    {
        Mesh = mesh;
        Surfaces = surfaces;
        _bindPositions = bindPositions;
        _vertexBoneNames = vertexBoneNames;
        _vertexDetachesAfterSpawn = vertexDetachesAfterSpawn;
        _particleSpawnCycles = new int[bindPositions.Length];
        Array.Fill(_particleSpawnCycles, int.MinValue);
    }

    public Mesh Mesh { get; }
    public static EquipmentEffectScene Empty { get; } = new(null!, [], [], [], []);
    public IReadOnlyList<EquipmentEffectSurface> Surfaces { get; }

    public IReadOnlyList<string> TextureNames => Surfaces
        .Select(static surface => surface.TextureName)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    /// <summary>Updates bound effects and emits a new fire/poison particle at each particle's next lifetime.</summary>
    public void ApplyPose(GrnAnimatedMesh animatedMesh, float deltaSeconds = 0.0f)
    {
        _particleElapsedSeconds += Math.Max(0.0f, deltaSeconds);
        var changed = false;
        for (var index = 0; index < _bindPositions.Length; index++)
        {
            var boneName = _vertexBoneNames[index];
            if (boneName is null)
            {
                continue;
            }

            if (_vertexDetachesAfterSpawn[index])
            {
                var phase = Math.Max(0.0f, Mesh.Vertices[index].Normal.Z - 1.0f);
                var spawnCycle = (int)MathF.Floor(
                    _particleElapsedSeconds / EmittedParticleLifetimeSeconds + phase);
                if (_particleSpawnCycles[index] == spawnCycle)
                    continue;

                _particleSpawnCycles[index] = spawnCycle;
            }

            if (!animatedMesh.TryTransformRigidPoint(boneName, _bindPositions[index], out var position))
                continue;

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
    ParticleTextureMode textureMode,
    float phase,
    Vector3 motionVector = default)
{
    public int IndexStart { get; } = indexStart;
    public int IndexCount { get; private set; } = indexCount;
    public string TextureName { get; } = textureName;
    public Vector4 Color { get; } = color;
    public ParticleTextureMode TextureMode { get; } = textureMode;
    public float Phase { get; } = phase;
    public Vector3 MotionVector { get; } = motionVector;

    internal void Extend(int additionalIndexCount) => IndexCount += additionalIndexCount;
}
