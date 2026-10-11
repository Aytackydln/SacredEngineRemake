using System.Numerics;
using System.Runtime.InteropServices;
using Sacred.Particles;
using Sacred.Particles.Particles;

namespace Sacred.World.Particles;

/// <summary>Stateful native flare factors and repeated random burst scheduling.</summary>
internal sealed class ParticleAuraPlayback
{
    private readonly SacredParticleDefinition _definition;
    private readonly SacredParticleHaloDefinition[] _halos;
    private readonly float[] _factors;
    public float Countdown { get; private set; }
    public ParticleAuraPlayback(SacredParticleDefinition definition)
    {
        _definition = definition;
        _halos = (definition.Halo is { } halo ? new[] { halo } : [])
            .Concat(definition.AdditionalHalos).ToArray();
        _factors = _halos.Select(h => h.Envelope?.InitialValue ?? 1).ToArray();
    }
    public void Advance(float age, float step)
    {
        for (var i = 0; i < _halos.Length; i++)
            if (_halos[i].Envelope is { } envelope)
                _factors[i] = envelope.Advance(_factors[i], age, step);
        if (_definition.RandomBurst is not null) Countdown -= step;
    }
    public float Factor(SacredParticleHaloDefinition halo) => _factors[Array.IndexOf(_halos, halo)];
    public WorldParticleAuraSnapshot Capture() => new(Countdown, (float[])_factors.Clone());
    public void Restore(WorldParticleAuraSnapshot snapshot)
    {
        if (snapshot.HaloFactors.Length != _factors.Length) throw new ArgumentException("Mismatched native halo state.");
        Countdown = snapshot.Countdown;
        snapshot.HaloFactors.CopyTo(_factors, 0);
    }
    public SacredParticleParameterSet? PrepareBurst(SacredParticleParameterSet parameters, Random random)
    {
        if (_definition.RandomBurst is not { } burst || Countdown >= 0) return null;
        Countdown = (float)(random.NextDouble() * burst.DelayWidth + burst.DelayMinimum);
        var z = (float)(random.NextDouble() * (burst.VerticalHalfRange * 2) - burst.VerticalHalfRange);
        var y = (float)(random.NextDouble() * (burst.Radius * 2) - burst.Radius);
        var x = (float)(random.NextDouble() * (burst.Radius * 2) - burst.Radius);
        var bytes = new byte[SacredParticleEmissionLayout.SerializedSize];
        var emission = parameters.Emission;
        MemoryMarshal.Write(bytes, in emission);
        var position = new Vector3(x, y, z);
        MemoryMarshal.Write(bytes.AsSpan(0x38), in position);
        return parameters with { Emission = MemoryMarshal.Read<SacredParticleEmissionLayout>(bytes) };
    }
}
