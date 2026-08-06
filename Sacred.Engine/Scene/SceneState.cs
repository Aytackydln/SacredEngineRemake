using System;
using System.Collections.Generic;
using System.Numerics;
using Sacred.Assets.Paks.Texture;
using Sacred.Granny;
using Sacred.Inventory.Effects;

namespace Sacred.Engine.Scene;

public sealed class SceneState
{
    private readonly List<SceneModel> _models = new(capacity: 32);

    public IReadOnlyList<SceneModel> Models => _models;
    public SceneLighting Lighting { get; } = new();
    public SceneDebugState Debug { get; } = new();
    public MinimapOverlayState Minimap { get; } = new();

    /// <summary>Changes only when model geometry or material bindings change.</summary>
    public ulong ModelSetRevision { get; private set; }

    public void AddModel(SceneModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        _models.Add(model);
        ModelSetRevision++;
    }

    public void SetModel(int index, SceneModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        _models[index] = model;
        ModelSetRevision++;
    }

    public void SetModelMesh(int index, Mesh mesh)
    {
        if (_models[index].SetMesh(mesh))
            ModelSetRevision++;
    }
}

public sealed class MinimapOverlayState
{
    public bool IsVisible { get; internal set; }
    public string DifficultyDisplayName { get; set; } = string.Empty;
}

public sealed class SceneDebugState
{
    public bool StairsMapVisible { get; set; }
    public bool BlockedAreasVisible { get; set; }
    public float ActorTerrainHeight { get; set; }
}

public sealed class SceneLighting
{
    public const float DefaultUnlitStaticSpriteWhiteNits = 380.0f;

    public Vector3 LightPosition { get; set; } = new(0.0f, 250.0f, 650.0f);
    public Vector3 DirectionToLight { get; set; } = Vector3.UnitZ;
    public Vector3 DirectionToSun { get; set; } = Vector3.UnitZ;
    public Vector3 LightColor { get; set; } = new(1.0f, 0.93f, 0.82f);
    public Vector3 AmbientColor { get; set; } = new(0.76f, 0.84f, 1.0f);
    public float AmbientIntensity { get; set; } = 0.28f;
    public float DiffuseIntensity { get; set; } = 0.85f;
    public float SpecularIntensity { get; set; } = 0.20f;
    public float Shininess { get; set; } = 24.0f;
    public float WorldQuadAmbientIntensity { get; set; } = 1.0f;
    public float UnlitStaticSpriteWhiteNits { get; set; } = DefaultUnlitStaticSpriteWhiteNits;
    public float NightBlend { get; set; }
    /// <summary>Normalized solar elevation: zero at/below the horizon and one at noon.</summary>
    public float SunHeight { get; set; } = 1.0f;
    public float ShadowOpacity { get; set; } = 0.42f;
}

/// <summary>A mutable scene instance with a transform cached for the render hot path.</summary>
public sealed class SceneModel
{
    private Matrix4x4 _transform;

    public SceneModel(
        string name,
        Mesh mesh,
        Vector3 position,
        Vector3 rotation,
        float scale = 1.0f,
        IReadOnlyDictionary<string, ModelTextureReference>? textureAliases = null,
        EquipmentEffectScene? equipmentEffects = null,
        float? groundPlaneZ = null)
    {
        Name = name;
        Mesh = mesh;
        Position = position;
        DepthAnchor = new Vector2(position.X, position.Y);
        Rotation = rotation;
        Scale = scale;
        TextureAliases = textureAliases;
        EquipmentEffects = equipmentEffects;
        GroundPlaneZ = groundPlaneZ ?? position.Z;
        RebuildTransform();
    }

    public string Name { get; }
    public Mesh Mesh { get; private set; }
    public Vector3 Position { get; private set; }
    public Vector2 DepthAnchor { get; private set; }
    public Vector3 Rotation { get; private set; }
    public float Scale { get; }
    public float GroundPlaneZ { get; private set; }
    public IReadOnlyDictionary<string, ModelTextureReference>? TextureAliases { get; }
    public EquipmentEffectScene? EquipmentEffects { get; }
    public Matrix4x4 Transform => _transform;

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
        if (position == Position && rotation == Rotation && depthAnchor == DepthAnchor && groundPlaneZ == GroundPlaneZ)
            return;

        Position = position;
        Rotation = rotation;
        DepthAnchor = depthAnchor;
        GroundPlaneZ = groundPlaneZ;
        RebuildTransform();
    }

    internal bool SetMesh(Mesh mesh)
    {
        if (ReferenceEquals(Mesh, mesh))
            return false;

        Mesh = mesh;
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

    private void RebuildTransform()
    {
        _transform = Matrix4x4.CreateScale(Scale) *
                     Matrix4x4.CreateFromYawPitchRoll(Rotation.X, Rotation.Y, Rotation.Z) *
                     Matrix4x4.CreateTranslation(Position);
    }
}
