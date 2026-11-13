using Sacred.Core.Pak.Creature;

namespace Sacred.GameplayHost.Scripting;

public sealed record WorldScriptHandRefreshContext(SacredCreatureTemplate Template,
    WorldCreatureScalarResultAssets Results, uint LocalReference, uint Players);

