using System.Collections.Generic;
using System.Numerics;
using Sacred.Granny;

namespace Sacred.Engine.Scene;

public sealed class SceneState
{
    public List<SceneModel> Models { get; } = new(capacity: 32);
    public SceneLighting Lighting { get; } = new();
}

public sealed class SceneLighting
{
    public Vector3 LightPosition { get; set; } = new(0.0f, 250.0f, 650.0f);
    public Vector3 LightColor { get; set; } = new(1.0f, 0.93f, 0.82f);
    public Vector3 AmbientColor { get; set; } = new(0.76f, 0.84f, 1.0f);
    public float AmbientIntensity { get; set; } = 0.28f;
    public float DiffuseIntensity { get; set; } = 0.85f;
    public float SpecularIntensity { get; set; } = 0.20f;
    public float Shininess { get; set; } = 24.0f;
}

public sealed record SceneModel(
    string Name,
    Mesh Mesh,
    Vector3 Position,
    Vector3 Rotation,
    float Scale = 1.0f
)
{
    public Matrix4x4 Transform =>
        Matrix4x4.CreateScale(Scale) *
        Matrix4x4.CreateFromYawPitchRoll(Rotation.X, Rotation.Y, Rotation.Z) *
        Matrix4x4.CreateTranslation(Position);
}
