using System.Numerics;

namespace Sacred.Particles;

/// <summary>stdCreation's moving point origin: release history at object +0x2088.
/// Interpolation advances only for births that fit the particle vector.</summary>
public sealed class ParticlePointEmissionPlayback
{
    private Vector3 _step;
    public Vector3 Released { get; private set; }
    public void Initialize(Vector3 origin) => Released = origin;
    public void Begin(float elapsed, float interval, Vector3 origin)
    {
        var delta = origin - Released;
        _step = new((float)(delta.X / (double)elapsed * interval),
            (float)(delta.Y / (double)elapsed * interval), (float)(delta.Z / (double)elapsed * interval));
    }
    public Vector3 Birth(int index) => new(
        (float)(Released.X + (double)_step.X * index),
        (float)(Released.Y + (double)_step.Y * index),
        (float)(Released.Z + (double)_step.Z * index));
    public void Commit(int count) => Released = Birth(count);
    public void Restore(Vector3 released) => Released = released;
}
