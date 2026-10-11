using Sacred.Particles;

namespace Sacred.World.Particles;

internal sealed partial class WorldParticleEmitter
{
    private readonly SacredMineSignFxPlayback? _mineSign;
    internal bool RequestMineUsed()
    {
        if (_mineSign is null) return false;
        _mineSign.RequestMineUsed(); return true;
    }
    private void UpdateMineSign(float seconds, List<WorldParticle> output)
    {
        var sign = _mineSign!;
        UpdateParticles(seconds);
        sign.Advance(seconds, emission =>
        {
            _parameterSets[0] = _parameterSets[0] with { Emission = emission };
            _batches[0].ApplyParameters(_parameterSets[0]);
            _emissionTime = sign.ElapsedTime;
            _emissionElapsed = 0; // Native count-mode birth has no pre-advance.
            if (_particleCount < _capacity) Spawn(_parameterSets[0]);
        });
        _age = sign.ElapsedTime;
        NativeRetired = !sign.Active;
        if (!NativeRetired) AppendDraw(_placement.Definition.Draw!, output);
    }
}
