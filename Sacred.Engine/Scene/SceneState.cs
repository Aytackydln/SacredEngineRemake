using System.Collections.Generic;
using System.Numerics;
using Sacred.Core.Assets;

namespace Sacred.Engine.Scene;

public sealed class SceneState
{
    public List<SceneModel> Models { get; } = new(capacity: 32);
    public SceneLighting Lighting { get; } = new();
}

public sealed class SceneLighting
{
    public Vector3 LightPosition { get; set; } = new(0.0f, 250.0f, 650.0f);
    public Vector3 LightColor { get; set; } = Vector3.One;
    public Vector3 AmbientColor { get; set; } = Vector3.One;
    public float AmbientIntensity { get; set; } = 0.35f;
    public float DiffuseIntensity { get; set; } = 0.75f;
    public float SpecularIntensity { get; set; } = 0.20f;
    public float Shininess { get; set; } = 24.0f;
}

public sealed record SceneModel(
    string Name,
    Mesh Mesh,
    Vector3 Position,
    Vector3 Rotation,
    GrnAsset? SourceModel = null
)
{
    public Matrix4x4 Transform => Matrix4x4.CreateFromYawPitchRoll(Rotation.X, Rotation.Y, Rotation.Z) * Matrix4x4.CreateTranslation(Position);
}
