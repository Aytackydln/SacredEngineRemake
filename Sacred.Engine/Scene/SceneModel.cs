using System;
using System.Collections.Generic;
using System.Numerics;
using Sacred.Assets.Paks.Texture;
using Sacred.Engine.Graphics.Skinning;
using Sacred.Granny.Animation;
using Sacred.Granny.Diagnostics;
using Sacred.Granny.Meshes;
using Sacred.Inventory.Effects;
using Sacred.Particles;
using Sacred.World.Geometry;

namespace Sacred.Engine.Scene;

/// <summary>A mutable scene instance with a transform cached for the render hot path.</summary>
public sealed class SceneModel : ISceneModelBounds
{
    private Matrix4x4 _transform;
    private readonly Vector3 _localBoundsCenter;
    private readonly float _localBoundsRadius;
    private readonly SceneModelBounds _bounds;
    private Vector3 _modelOffset;
    private Matrix4x4 _modelProjection = Matrix4x4.Identity;
    // GRN extraction centers mesh vertices; world props retain their authored pivot here.
    private readonly Vector3 _sourceOriginOffset;

    public SceneModel(
        string name,
        Mesh mesh,
        Vector3 position,
        Vector3 rotation,
        float scale = 1.0f,
        IReadOnlyDictionary<string, ModelTextureReference>? textureAliases = null,
        EquipmentEffectScene? equipmentEffects = null,
        float? groundPlaneZ = null,
        Vector3 sourceOriginOffset = default,
        uint blockRadius = 0)
    {
        Name = name;
        Geometry = SceneModelGeometry.ForMesh(mesh);
        Position = position;
        DepthAnchor = new Vector2(position.X, position.Y);
        Rotation = rotation;
        Scale = scale;
        _sourceOriginOffset = sourceOriginOffset;
        TextureAliases = textureAliases;
        EquipmentEffects = equipmentEffects;
        EquipmentEffects?.ResetNativeEffects();
        GroundPlaneZ = groundPlaneZ ?? position.Z;
        // Gold TypeManager::getBlockRadius (0x428CE0) substitutes 50 for zero.
        GroundShadowRadius = blockRadius == 0 ? 50.0f : blockRadius;
        (_localBoundsCenter, _localBoundsRadius) = CalculateBounds(mesh);
        _bounds = new(_localBoundsCenter);
        RebuildTransform();
    }

    public string Name { get; }
    public SceneModelGeometry Geometry { get; private set; }
    public Mesh Mesh => Geometry.GetCpuRenderMesh();
    public Vector3 Position { get; private set; }
    public Vector2 DepthAnchor { get; private set; }
    /// <summary>World props use their authored tile ordering without the character occlusion bias.</summary>
    public bool IsWorldObject { get; init; }
    public Vector3 Rotation { get; private set; }
    public float Scale { get; }
    /// <summary>Resolved Items.pak blocking radius in authored model units, independent of the pose.</summary>
    public float GroundShadowRadius { get; }
    /// <summary>Contact-shadow half-extents in the scene camera's ground plane.</summary>
    public Vector2 GroundShadowHalfExtents => new(
        GroundShadowRadius * Scale * MathF.Abs(_modelProjection.M11),
        GroundShadowRadius * Scale * MathF.Abs(_modelProjection.M22));
    public SceneBoundsSnapshot VisibilityBounds => Geometry.Animation?.Playback.Sink is GpuPoseRequestSink
        ? new(OcclusionProbeCenter, float.PositiveInfinity, 0)
        : _bounds.Get(Geometry.Pose, _transform, _localBoundsRadius * Scale * MathF.Max(
        MathF.Abs(_modelProjection.M11), MathF.Max(MathF.Abs(_modelProjection.M22), MathF.Abs(_modelProjection.M33))));
    public float WorldBoundsRadius => VisibilityBounds.Radius;
    public float GroundPlaneZ { get; private set; }
    /// <summary>Absolute model-camera position derived from the gameplay tile anchor.</summary>
    public Vector3 RenderPosition
    {
        get
        {
            var modelWorld = IsometricProjection.WorldToModel(Position.X, Position.Y);
            return new Vector3(modelWorld, Position.Z);
        }
    }
    public Vector3 VisualCenter => VisibilityBounds.Center;
    /// <summary>Stable body probe anchor, independent of conservative animated visibility envelopes.</summary>
    public Vector3 OcclusionProbeCenter => Vector3.Transform(_localBoundsCenter, _transform);
    public IReadOnlyDictionary<string, ModelTextureReference>? TextureAliases { get; }
    public EquipmentEffectScene? EquipmentEffects { get; }
    public IReadOnlySet<(string TextureName, ParticleTextureMode Mode)>? DisabledEquipmentEffects { get; internal set; }
    public Matrix4x4 Transform => _transform;
    public Matrix4x4 ModelProjection => _modelProjection;

    /// <summary>Adapts an authored model camera to the scene camera after model facing.</summary>
    public void SetModelProjection(Matrix4x4 projection)
    {
        _modelProjection = projection;
        RebuildTransform();
    }

    /// <summary>Applies an authored local-model animation offset without changing its world tile anchor.</summary>
    public void SetModelOffset(Vector3 offset)
    {
        if (offset == _modelOffset)
            return;

        _modelOffset = offset;
        RebuildTransform();
    }

    public void SetPose(Vector3 position, Vector3 rotation)
    {
        SetPose(position, rotation, new Vector2(position.X, position.Y));
    }

    public void SetPose(Vector3 position, Vector3 rotation, Vector2 depthAnchor)
    {
        SetPose(position, rotation, depthAnchor, position.Z);
    }

    public void SetPose(Vector3 position, Vector3 rotation, Vector2 depthAnchor, float groundPlaneZ)
    {
        SetPose(position, rotation, depthAnchor, groundPlaneZ, null);
    }

    public void SetPoseFollowingCamera(
        Vector3 position,
        Vector3 rotation,
        Vector2 depthAnchor,
        float groundPlaneZ,
        Vector2 cameraWorldMovement)
    {
        SetPose(position, rotation, depthAnchor, groundPlaneZ, cameraWorldMovement);
    }

    private void SetPose(
        Vector3 position,
        Vector3 rotation,
        Vector2 depthAnchor,
        float groundPlaneZ,
        Vector2? cameraWorldMovement)
    {
        if (position == Position && rotation == Rotation && depthAnchor == DepthAnchor && groundPlaneZ == GroundPlaneZ)
            return;

        Position = position;
        Rotation = rotation;
        DepthAnchor = depthAnchor;
        GroundPlaneZ = groundPlaneZ;
        RebuildTransform(cameraWorldMovement);
    }

    internal bool SetMesh(Mesh mesh)
    {
        if (Geometry.UsesCpuMesh(mesh))
            return false;

        Geometry = SceneModelGeometry.ForMesh(mesh);
        // Animation changes vertices, not the model's cached visibility extent or authored radius.
        // GPU vertex freshness is tracked independently by Mesh.VertexRevision.
        return true;
    }

    internal bool SetGeometry(SceneModelGeometry geometry)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        if (ReferenceEquals(Geometry, geometry)) return false;
        Geometry = geometry;
        return true;
    }

    internal bool SetAnimatedMesh(GrnAnimatedMesh animation)
    {
        if (ReferenceEquals(Geometry.Animation, animation)) return false;
        Geometry = SceneModelGeometry.ForCpuSkinning(animation);
        return true;
    }

    public ModelTextureReference ResolveTextureReference(string? textureName)
    {
        if (string.IsNullOrWhiteSpace(textureName))
            return new ModelTextureReference(string.Empty, TextureAnimation.None);

        return TextureAliases is not null && TextureAliases.TryGetValue(textureName, out var alias)
            ? alias
            : ModelTextureReference.Static(textureName);
    }

    private void RebuildTransform(Vector2? cameraWorldMovement = null)
    {
        var previous = _transform;
        var rotation = Matrix4x4.CreateFromYawPitchRoll(Rotation.X, Rotation.Y, Rotation.Z);
        var renderPosition = RenderPosition + Vector3.Transform(_modelOffset, rotation * _modelProjection);
        _transform = Matrix4x4.CreateTranslation(_sourceOriginOffset) *
                     Matrix4x4.CreateScale(Scale) *
                     rotation *
                     _modelProjection *
                     Matrix4x4.CreateTranslation(renderPosition);
        if (previous != default)
            EquipmentEffects?.RebaseNativeEffects(previous, _transform);
    }

    private static (Vector3 Center, float BoundsRadius) CalculateBounds(Mesh mesh)
    {
        using var measurement = AnimationPerformance.Measure(AnimationCpuStage.Bounds);
        if (mesh.Vertices.Length == 0)
            return (Vector3.Zero, 0.0f);

        var minimum = mesh.Vertices[0].Position;
        var maximum = minimum;
        foreach (var vertex in mesh.Vertices.AsSpan(1))
        {
            minimum = Vector3.Min(minimum, vertex.Position);
            maximum = Vector3.Max(maximum, vertex.Position);
        }

        var size = maximum - minimum;
        var boundsRadius = size.Length() * 0.5f;
        return ((minimum + maximum) * 0.5f, boundsRadius);
    }
}
