using System.Numerics;

namespace Sacred.Particles;

/// <summary>stdCreationOnLine endpoint interpolation. Release state advances only
/// by intervals that actually produced births, including capacity-limited updates.</summary>
public sealed class ParticleLineEmissionPlayback
{
    public ParticleEmissionLine Current { get; private set; }
    public ParticleEmissionLine Released { get; private set; }
    private ParticleEmissionLine _step;
    public void Set(ParticleEmissionLine line)
    {
        if (!line.IsFinite) throw new ArgumentException("Non-finite emission line.", nameof(line));
        Current = line;
    }
    public void Initialize(Vector3 offset) => Released = new(Current.Start + offset, Current.End + offset);
    public void Begin(float elapsed, float interval, Vector3 offset)
    {
        _step = new(Step(Current.Start + offset - Released.Start, interval, elapsed),
            Step(Current.End + offset - Released.End, interval, elapsed));
    }
    public Vector3 Birth(int index, double random) => Interpolate(
        Add(Released.Start, _step.Start, index), Add(Released.End, _step.End, index), random);
    public Vector3 FirstBirth(Vector3 offset, double random) => Interpolate(Current.Start + offset, Current.End + offset, random);
    public void Commit(int count) => Released = new(Add(Released.Start, _step.Start, count), Add(Released.End, _step.End, count));
    public void Restore(ParticleEmissionLine current, ParticleEmissionLine released)
    {
        if (!released.IsFinite) throw new ArgumentException("Non-finite released line.", nameof(released));
        Set(current); Released = released;
    }
    // Native rounds endpoint deltas and each interval's displacement to float, then
    // performs multiply/add in the x87 register before writing the resulting point.
    private static Vector3 Step(Vector3 delta, float interval, float elapsed) => new(
        (float)(delta.X / (double)elapsed * interval), (float)(delta.Y / (double)elapsed * interval),
        (float)(delta.Z / (double)elapsed * interval));
    private static Vector3 Add(Vector3 point, Vector3 step, int count) => new(
        (float)(point.X + (double)step.X * count), (float)(point.Y + (double)step.Y * count),
        (float)(point.Z + (double)step.Z * count));
    private static Vector3 Interpolate(Vector3 start, Vector3 end, double random) => new(
        (float)(start.X + (end.X - start.X) * random), (float)(start.Y + (end.Y - start.Y) * random),
        (float)(start.Z + (end.Z - start.Z) * random));
}
