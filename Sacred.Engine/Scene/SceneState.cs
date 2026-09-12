using System;
using System.Collections.Generic;
using System.Numerics;
using Sacred.Assets.Paks.Texture;
using Sacred.Core.Pak.Items;
using Sacred.Core.World;
using Sacred.Core.World.Pathing;
using Sacred.Core.World.Sector;
using Sacred.Granny.Meshes;
using Sacred.Inventory.Effects;
using Sacred.World.Geometry;

namespace Sacred.Engine.Scene;

public sealed class SceneState
{
    private readonly List<SceneModel> _models = new(capacity: 32);

    public IReadOnlyList<SceneModel> Models => _models;
    public SceneLighting Lighting { get; } = new();
    public SceneDebugState Debug { get; } = new();
    public IndoorSceneState Indoor { get; } = new();
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

    /// <summary>
    /// Replaces model-backed world scenery while retaining the player at index zero.
    /// The player controller relies on that stable slot for animation updates.
    /// </summary>
    public void SetWorldModels(IReadOnlyList<SceneModel> models)
    {
        if (_models.Count == 0)
            throw new InvalidOperationException("World models require the player model to be initialized first.");

        if (_models.Count - 1 == models.Count)
        {
            var matches = true;
            for (var index = 0; index < models.Count; index++)
            {
                if (ReferenceEquals(_models[index + 1], models[index]))
                    continue;

                matches = false;
                break;
            }

            if (matches)
                return;
        }

        _models.RemoveRange(1, _models.Count - 1);
        for (var index = 0; index < models.Count; index++)
            _models.Add(models[index]);
        ModelSetRevision++;
    }
}

public sealed class IndoorSceneState
{
    public IndoorTileGroup? ActiveGroup { get; internal set; }
}

public sealed class MinimapOverlayState
{
    public bool IsVisible { get; internal set; }
    public string DifficultyDisplayName { get; set; } = string.Empty;
    public string RegionDisplayName { get; set; } = string.Empty;
}

public sealed class SceneDebugState
{
    public bool OverlaysVisible { get; set; } = true;
    public bool PanelVisible { get; set; }
    public bool StairsMapVisible { get; set; }
    public bool BlockedAreasVisible { get; set; }
    public bool TerrainTopologyVisible { get; set; }
    public bool TileCoordinatesVisible { get; set; }
    public WorldPathFlags VisiblePathFlags { get; set; }
    public WldxTileFlags VisibleTileFlags { get; set; }
    public WldxTerrainSurface VisibleSurfaceFlags { get; set; }
    public SectorEnvironmentFlags VisibleSectorFlags { get; set; }
    public StaticObjectFlags VisibleStaticObjectFlags { get; set; }
    public SacredItemGraphicFlags VisibleItemGraphicFlags { get; set; }
    public int ItemDescriptorByteOffset { get; set; }
    public byte VisibleItemDescriptorByteBits { get; set; }
    public byte ItemDescriptorByteMatchValue { get; set; }
    public bool ItemDescriptorByteMatchEnabled { get; set; }
    public bool ItemDescriptorByteValuesVisible { get; set; }
    public bool MovementFlagTilesVisible { get; set; }
    public bool EntranceTilesVisible { get; set; }
    public bool TerrainSurfacesVisible { get; set; }
    public bool VisualElevationVisible { get; set; }
    public bool GameplayElevationVisible { get; set; }
    public bool BakedLightingVisible { get; set; }
    public bool SectorBoundsVisible { get; set; }
    public bool WorldLightBoundsVisible { get; set; }
    public bool StaticSpriteBoundsVisible { get; set; }
    /// <summary>Shows each rendered 3D model's name and transform anchor in the world overlay.</summary>
    public bool ModelNamesVisible { get; set; }
    public uint? HoveredStaticObjectId { get; set; }
    public float ActorTerrainHeight { get; set; }
}

public sealed class SceneLighting
{
    public Vector3 LightPosition { get; set; } = new(0.0f, 250.0f, 650.0f);
    public Vector3 DirectionToLight { get; set; } = Vector3.UnitZ;
    public Vector3 DirectionToSun { get; set; } = Vector3.UnitZ;
    public Vector3 LightColor { get; set; } = new(1.0f, 0.93f, 0.82f);
    public Vector3 AmbientColor { get; set; } = new(0.76f, 0.84f, 1.0f);
    public float AmbientIntensity { get; set; } = 0.28f;
    public float DiffuseIntensity { get; set; } = 0.85f;
    public float SpecularIntensity { get; set; } = 0.20f;
    public float Shininess { get; set; } = 24.0f;
    public Vector3 WorldSurfaceAmbientColour { get; set; } = Vector3.One;
    public float NightBlend { get; set; }
    public float PlayerLightDiameter { get; set; }
    public Vector3 PlayerLightColour { get; set; } = Vector3.One;
    public float PlayerLightOpacity { get; set; } = 0.35f;
    /// <summary>Normalized solar elevation: zero at/below the horizon and one at noon.</summary>
    public float SunHeight { get; set; } = 1.0f;
    /// <summary>Solar shadow opacity used by objects on the outdoor surface.</summary>
    public float OutdoorShadowOpacity { get; set; } = 0.65f;
    /// <summary>Contact-shadow opacity used by objects on the active indoor surface.</summary>
    public float IndoorShadowOpacity { get; set; }
    public float ShadowOpacity { get; set; } = 0.65f;
    public SceneShadowMode ShadowMode { get; set; } = SceneShadowMode.Directional;
}

public enum SceneShadowMode
{
    None,
    Directional,
    SoftContact,
}

/// <summary>A mutable scene instance with a transform cached for the render hot path.</summary>
public sealed class SceneModel
{
    private Matrix4x4 _transform;
    private Vector3 _localBoundsCenter;
    private float _localBoundsRadius;
    private Vector3 _modelOffset;

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
        EquipmentEffects?.ResetNativeEffects();
        GroundPlaneZ = groundPlaneZ ?? position.Z;
        (_localBoundsCenter, _localBoundsRadius, GroundShadowRadius) = CalculateBounds(mesh);
        RebuildTransform();
    }

    public string Name { get; }
    public Mesh Mesh { get; private set; }
    public Vector3 Position { get; private set; }
    public Vector2 DepthAnchor { get; private set; }
    public Vector3 Rotation { get; private set; }
    public float Scale { get; }
    public float GroundShadowRadius { get; private set; }
    /// <summary>Conservative local mesh-sphere radius used by the renderer's early visibility test.</summary>
    public float WorldBoundsRadius => _localBoundsRadius * Scale;
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
    public Vector3 VisualCenter => Vector3.Transform(_localBoundsCenter, _transform);
    public IReadOnlyDictionary<string, ModelTextureReference>? TextureAliases { get; }
    public EquipmentEffectScene? EquipmentEffects { get; }
    public Matrix4x4 Transform => _transform;

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
        if (ReferenceEquals(Mesh, mesh))
            return false;

        Mesh = mesh;
        (_localBoundsCenter, _localBoundsRadius, GroundShadowRadius) = CalculateBounds(mesh);
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
        var renderPosition = RenderPosition + Vector3.Transform(_modelOffset, rotation);
        _transform = Matrix4x4.CreateScale(Scale) *
                     rotation *
                     Matrix4x4.CreateTranslation(renderPosition);
        if (previous != default)
            EquipmentEffects?.RebaseNativeEffects(previous, _transform);
    }

    private static (Vector3 Center, float BoundsRadius, float GroundShadowRadius) CalculateBounds(Mesh mesh)
    {
        if (mesh.Vertices.Length == 0)
            return (Vector3.Zero, 0.0f, 6.0f);

        var minimum = mesh.Vertices[0].Position;
        var maximum = minimum;
        foreach (var vertex in mesh.Vertices.AsSpan(1))
        {
            minimum = Vector3.Min(minimum, vertex.Position);
            maximum = Vector3.Max(maximum, vertex.Position);
        }

        var size = maximum - minimum;
        var horizontalRadius = MathF.Max(size.X, size.Y) * 0.575f;
        var heightRadius = size.Z * 0.10f;
        var boundsRadius = size.Length() * 0.5f;
        return ((minimum + maximum) * 0.5f,
            boundsRadius,
            MathF.Max(6.0f, MathF.Max(horizontalRadius, heightRadius)));
    }
}
