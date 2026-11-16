using Sacred.Assets.Paks.Weapon;
using Sacred.Assets.World;
using Sacred.Core.GameBin.Scripts;
using Sacred.Core.Runtime;
using Sacred.Core.World;
using Sacred.GameplayHost.Scripting;
using Sacred.Network.Gameplay;

namespace Sacred.GameplayHost.Networking;

/// <summary>Compose the existing native-compared startup producers with actual
/// session grants and advertised difficulty for the absent-hero diagnostic.
/// Selected-character production entry must restore its pre-script actor first;
/// WorldHostInitializationSession guards that boundary. No readiness is inferred.</summary>
internal static class WorldHostStartupPreparation
{
    public static WorldHostStartupState Run(string installation, SacredCampaignFiles campaign,
        byte slot, GoldWorldReplica replicas, GoldObjectReferencePool references,
        Action<string> log, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (replicas.Session.GameFlags is null) throw new InvalidOperationException("Host difficulty needs actual session flags.");
        var source = SacredWorldTriggerArchive.Load(Path.Combine(installation, "World", "Triggers.PAK"));
        var sectorIndex = SacredSectorIndex.Read(Path.Combine(installation, "World", "sectors.keyx"));
        var sectors = sectorIndex.CreateGrid();
        var triggers = new WorldScriptTriggerRegistry(source, replicas.TriggerReferences, sectors);
        var templates = SacredObjectTemplateArchive.Load(installation);
        var objects = new WorldScriptObjectWorld(templates, references, sectors, triggers, slot, 0, hasActiveHero: false);
        objects.Creatures = new(WorldScriptCreatureAssets.Load(installation, templates, sectorIndex), references,
            objects.AllocationCounter, new(slot, replicas.Session.Difficulty, false, 0, () => unchecked((uint)Environment.TickCount))
                { EffectProjection = WorldStartupEffectProjection.ComparedGold },
            new SacredRandomSequence(unchecked((uint)Environment.TickCount)), log);
        var scripts = WorldCampaignScripts.Load(campaign);
        var state = WorldScriptStartupInspection.Run(scripts, log, cancellationToken, triggers, objects);
        return new(state, triggers, objects,
            WorldScriptStartupDependencies.InspectCreature(installation, scripts, state, templates, log));
    }
}
