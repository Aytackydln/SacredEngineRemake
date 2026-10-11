using Sacred.Particles;

namespace Sacred.World.Particles;

internal sealed partial class WorldParticleEmitter
{
    private readonly SacredBloodFountainFxPlayback? _bloodFountain;
    private void UpdateBloodFountain(float seconds, List<WorldParticle> output)
    {
        var fountain = _bloodFountain!;
        fountain.Advance(seconds);
        _age = fountain.ElapsedTime;
        _emissionTime = _age;
        if (_parameterSets[0].Emission.Velocity.Value != fountain.Emission.Velocity.Value)
        {
            _parameterSets[0] = _parameterSets[0] with { Emission = fountain.Emission };
            _batches[0].ApplyParameters(_parameterSets[0]);
        }
        UpdateParticles(seconds);
        // Native update still emits on the retiring step. Object-manager removal follows it.
        EmitParticles(seconds);
        NativeRetired = !fountain.Active;
        if (!NativeRetired) AppendDraw(_placement.Definition.Draw!, output);
    }
}
