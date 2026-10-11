using Sacred.Particles;
using Sacred.Particles.Diagnostics;

namespace Sacred.World.Particles;

internal sealed partial class WorldParticleEmitter
{
    private void EmitRadialBurst(SacredParticleRadialBurst radial)
    {
        using var scope = ParticlePerformance.Measure(ParticleCpuStage.Births);
        // Native checks elapsed == 0, so repeated zero-time updates replace the whole vector.
        foreach (var batch in _batches) batch.Clear();
        _particleCount = 0;
        ParticlePerformance.RecordBirths(_capacity);
        int NextRandom() => _random.Next(32768);
        Func<int> random = NextRandom;
        for (var i = 0; i < _capacity; i++)
        {
            _batches[0].Add(radial.Create(random, _nextDrawOrder++));
            _particleCount++;
        }
    }
}
