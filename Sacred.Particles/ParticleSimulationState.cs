using System.Numerics;

namespace Sacred.Particles;

/// <summary>Renderer-independent live state in native local simulation coordinates.</summary>
public struct ParticleSimulationState
{
    public Vector3 Position;
    public Vector3 Velocity;
    public float Gravity;
    public float Size;
    public float Fade;
    public float Rotation;
    public float AngularVelocity;
    public int AtlasCell;
    public int DrawOrder;
}
