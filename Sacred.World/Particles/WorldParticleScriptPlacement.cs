using Sacred.Core.GameBin.Scripts;
using Sacred.Particles;

namespace Sacred.World.Particles;

/// <summary>
/// A literal particle creation declared by the selected compiled world script.
/// Script control flow has not yet been evaluated, so this is not necessarily a
/// currently active particle system.
/// </summary>
public sealed record WorldParticleScriptPlacement(
    int ScriptOffset,
    SacredScriptCreateObject Creation,
    SacredParticleDefinition Definition,
    float WorldX,
    float WorldY);
