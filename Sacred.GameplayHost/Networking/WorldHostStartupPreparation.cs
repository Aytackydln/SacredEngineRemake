using Sacred.Assets.Paks.Weapon;
using Sacred.Assets.World;
using Sacred.Core.GameBin.Scripts;
using Sacred.Core.Runtime;
using Sacred.Core.World;
using Sacred.GameplayHost.Scripting;
using Sacred.Network.Gameplay;

namespace Sacred.GameplayHost.Networking;

/// <summary>Compose the existing native-compared startup producers with actual
/// session grants and difficulty. Selected-character startup reuses the entered
/// hero's factories, random sequence, counter and loaded terrain. Missing current
/// hero/team/statistics inputs remain guarded; no readiness is inferred.</summary>
internal static class WorldHostStartupPreparation
{
    public static WorldHostStartupState Run(string installation, SacredCampaignFiles campaign,
        byte slot, GoldWorldReplica replicas, GoldObjectReferencePool references,
        Action<string> log, CancellationToken cancellationToken, WorldHostActiveHeroBinding? activeHero,
        WorldHostStartupRequest? request)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (replicas.Session.GameFlags is null) throw new InvalidOperationException("Host difficulty needs actual session flags.");
        if (activeHero is not null)
        {
            activeHero.RequireCurrent();
            (request ?? throw new InvalidOperationException("Entered host startup requires its explicit world operation.")).RequireFreshWorld();
            if (activeHero.Actor.Base.ObjectReference != slot || activeHero.Factory.Difficulty != replicas.Session.Difficulty ||
                !string.Equals(Path.GetFullPath(installation).TrimEnd(Path.DirectorySeparatorChar),
                    Path.GetFullPath(activeHero.Installation).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Startup must use this entered hero's installation, admitted slot and actual difficulty.");
            log($"Host startup request: fresh world, command {request.CommandMode}/{request.Value}, Engine mode 0x{request.EngineMode:X8}; same current actor and loaded terrain.");
        }
        var restoreSource = activeHero is null ? null : WorldHostSectorRestoreSource.FromFreshWorld(request!);
        var source = SacredWorldTriggerArchive.Load(Path.Combine(installation, "World", "Triggers.PAK"));
        var sectorIndex = SacredSectorIndex.Read(Path.Combine(installation, "World", "sectors.keyx"));
        var sectors = sectorIndex.CreateGrid();
        var triggers = new WorldScriptTriggerRegistry(source, replicas.TriggerReferences, sectors);
        var templates = SacredObjectTemplateArchive.Load(installation);
        var objects = activeHero is null
            ? new WorldScriptObjectWorld(templates, references, sectors, triggers, slot, 0, hasActiveHero: false)
            : new WorldScriptObjectWorld(templates, references, sectors, triggers, slot, activeHero.Counter, hasActiveHero: true);
        var automaticLevels = activeHero is null ? null : new WorldHostStartupAutomaticLevels(activeHero, replicas.Session.Teams, sectorIndex);
        var runtime = new WorldScriptCreatureRuntime(slot, replicas.Session.Difficulty, false,
            activeHero is null ? 0u : replicas.Session.Lobby.Control.Roster.NativePlayerCount, () => unchecked((uint)Environment.TickCount))
        { EffectProjection = WorldStartupEffectProjection.ComparedGold, SessionGameFlags = replicas.Session.GameFlags,
                AutomaticLevelInputs = automaticLevels is null ? null : automaticLevels.Read };
        objects.Creatures = new(WorldScriptCreatureAssets.Load(installation, templates, sectorIndex), references,
            objects.AllocationCounter, runtime,
            activeHero?.Random ?? new SacredRandomSequence(unchecked((uint)Environment.TickCount)), log, activeHero);
        if (activeHero is not null) log($"Host startup player count: {runtime.Players?.ToString() ?? "unknown; complete current roster required"}.");
        var scripts = WorldCampaignScripts.Load(campaign);
        WorldScriptExecutionContext state;
        try { state = WorldScriptStartupInspection.Run(scripts, log, cancellationToken, triggers, objects); }
        finally { automaticLevels?.EndStartup(); }
        if (automaticLevels is not null) state.PostStartup.EnterBoundActorScope = automaticLevels.BeginCampaign;
        return new(state, triggers, objects,
            WorldScriptStartupDependencies.InspectCreature(installation, scripts, state, templates, log))
        { SectorRestoreSource = restoreSource };
    }
}
