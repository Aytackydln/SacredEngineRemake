using System.Runtime.InteropServices;
using Sacred.Particles;

namespace Sacred.World.Particles;

internal sealed partial class WorldParticleEmitter
{
    private float _orbitAngle;
    private ParticlePointEmissionPlayback? _point;
    private bool OrbitFinished => _placement.Definition.OrbitEmission is { } orbit && _orbitAngle >= orbit.LimitAngle;

    private void PrepareOrbit()
    {
        if (_placement.Definition.OrbitEmission is not { } orbit || OrbitFinished) return;
        var emission = _parameterSets[0].Emission;
        var bytes = MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref emission, 1));
        // The updater starts with (radius, 0, 0), then applies its XY rotation.
        Span<float> position = stackalloc float[] { (float)(Math.Cos(_orbitAngle) * orbit.Radius),
            (float)(Math.Sin(_orbitAngle) * orbit.Radius), 0 };
        MemoryMarshal.AsBytes(position).CopyTo(bytes.Slice(0x38, 12));
        _parameterSets[0] = _parameterSets[0] with { Emission = emission };
        _batches[0].ApplyParameters(_parameterSets[0]);
    }

    private void AdvanceOrbit(float dt)
    {
        if (_placement.Definition.OrbitEmission is { } orbit)
            _orbitAngle = (float)(_orbitAngle + (double)dt * orbit.AngularSpeed);
    }
}
