using Sacred.Assets.World;
using Sacred.Core.GameBin.Scripts;
using Sacred.Core.World;
using Sacred.Network.Gameplay;

namespace Sacred.GameplayHost.Scripting;

/// <summary>Runs authored startup in isolated state and exposes its first missing dependency.
/// Inspection never uploads a partial world or mutates a live participant session.</summary>
public static class WorldScriptStartupInspection
{
    /// <summary>Loads authored host dependencies for an isolated startup inspection.</summary>
    public static WorldScriptExecutionContext RunFromInstallation(string gameDirectory,
        SacredCampaignFiles files, Action<string> log, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var source = SacredWorldTriggerArchive.Load(Path.Combine(gameDirectory, "World", "Triggers.PAK"));
        var sectors = SacredSectorIndex.Read(Path.Combine(gameDirectory, "World", "sectors.keyx")).CreateGrid();
        var triggers = new WorldScriptTriggerRegistry(source, new GoldTriggerReferencePool(), sectors);
        return Run(WorldCampaignScripts.Load(files), log, cancellationToken, triggers);
    }

    public static WorldScriptExecutionContext Run(WorldCampaignScripts scripts, Action<string> log,
        CancellationToken cancellationToken = default, WorldScriptTriggerRegistry? triggers = null)
        => Run(scripts, log, cancellationToken, triggers, null);

    public static WorldScriptExecutionContext Run(WorldCampaignScripts scripts, Action<string> log,
        CancellationToken cancellationToken, WorldScriptTriggerRegistry? triggers, WorldScriptObjectWorld? objects)
    {
        ArgumentNullException.ThrowIfNull(scripts);
        ArgumentNullException.ThrowIfNull(log);
        var definitions = WorldScriptDefinitions.Read(File.ReadAllBytes(scripts.Files.DefPosPath));
        var creatures = objects?.Creatures;
        WorldScriptExecutionContext? context = null;
        context = new WorldScriptExecutionContext(definitions, scripts.Functions, scripts.FunctionCode, multiplayer: true,
            random: creatures is null ? null : creatures.Random.Next,
            positionAllowed: creatures?.Terrain is { } terrain ? (x,y) => terrain.IsAllowed(context!,x,y) : null)
        { StartupParameters = scripts.Initialization is { } initialization ? new(initialization.CreateParameterBlock()) : new(),
            VariableGlobals = scripts.Initialization is { } globals ? new(globals.CreateVariableGlobals()) : new(),
            RegionReplacements = scripts.Initialization is { } replacements ?
                Sacred.Assets.GameBin.SacredScriptRegionReplacementTable.Read(replacements.CreateRegionReplacements()) : null,
            EquipmentPools = scripts.EquipmentPoolArchive is { } pools ? new(pools) : new(),
            EquipmentPoolReload = scripts.EquipmentPoolArchive,
            Quests = new(scripts.Quests), WorldTriggers = triggers, ObjectWorld = objects, CreatureWorld = creatures, LocalPlayerSlot = creatures?.LocalPlayerSlot, HasActiveHero = objects?.HasActiveHero, ResourceText = creatures?.ResourceText, MapIcons = creatures?.MapIcons, ScriptRegions = creatures?.ScriptRegions, Mouseovers = creatures?.Mouseovers, Hideouts = creatures?.Hideouts };
        log($"Network startup execution: {scripts.Files.Name}; {context.Definitions.Records.Count} authored definitions, {context.Functions.Count} functions loaded.");
        try { new WorldScriptExecutor(context).ExecuteStartup(scripts.StartupCode, cancellationToken); }
        catch (WorldScriptExecutionFailure failure) { log($"Network startup blocked: {failure.Message}"); }
        log($"Network startup state: {context.ExecutedCommands} executed commands; {context.Variables.Records.Count} variables, " +
            $"{context.Definitions.Records.Count} definitions, {context.Triggers.Count} base triggers; complete {context.StartupCompleted}.");
        if (context.StartupCompleted) log("Startup bytecode complete; full banks, fixed-global provenance, initial sector work and host upload/readiness remain separate requirements.");
        log($"Post-startup campaign callback complete: {context.PostStartup.IsCompleted}; explicit connection gates and actor binding are required.");
        foreach (var pair in context.Coverage.OrderBy(pair => pair.Key)) log($"  Executed opcode {pair.Key}: {pair.Value}.");
        log($"  Variable global writes: {context.VariableGlobals.Mutations.Count}; unresolved indices {context.VariableGlobals.UnresolvedIndexCount}; original initialization required before application.");
        if (context.VariableGlobals.InitialValues is not null)
            log($"  Additional-global value provenance: {WorldScriptVariableGlobals.ValueCount - context.VariableGlobals.Analyze().UnknownValueCount}/150 values established across possible destinations; strict caller replay and world-bank writing remain separate.");
        if (context.Quests is { } quests) log($"  Quest rows: {quests.Count}; active {Enumerable.Range(0,quests.Count).Count(index => (quests.Get(index).Flags & SacredScriptQuestLayout.ActiveFlag) != 0)}; callbacks remain required for command completion.");
        if (context.Quests is { } questState) log($"  Prepared quest journal: {questState.Journal.Count} complete runtime records; actual journal-index pairs retained privately.");
        log($"  Local script GUI requests: {context.LocalGuiRequests.Count}; ordered submissions retained privately, presentation delivery pending.");
        log($"  Deferred object states: {context.DeferredObjectStates.Records.Count}; private save/replay compared, target open/lock effects pending; not bank-2 callback rows.");
        log($"  Ordered object/NPC callbacks: {context.ObjectCallbacks.Records.Count}; registration is separate from invocation.");
        log($"  Executed 80-byte registrations: {context.Registrations.Count} (includes native reserved entry zero).");
        log($"  Dialog associations: {context.DialogAssociations.Records.Count}; {context.DialogAssociations.Records.Count(row => !row.HasCompleteBytes)} rows lack raw caller bytes. Explicit normalized bank-3 section-5 serialization uses the audited 14-byte constructor layout; full banks remain pending.");
        log($"  Equipment pools: {context.EquipmentPools.EntryCount} retained entries, {context.EquipmentPools.DeclarationCount} executed declarations, peak {context.EquipmentPools.PeakEntryCount}; post-StartCode reload {(context.EquipmentPoolReload is null ? "absent" : "configured")}.");
        if (context.MapIcons is { } mapIcons) log($"  Prepared startup map icons: {mapIcons.Records.Count}; retained privately for complete world-bank upload.");
        if (context.Mouseovers is { } mouseovers) log($"  Prepared startup mouseovers: {mouseovers.Pending.Count} pending, {mouseovers.Active.Active.Count} active; terrain marking required before command completion.");
        if (creatures?.Effects is { } effects) log($"  Prepared startup Effects: {effects.Created.Count}; complete particle messages retained privately.");
        if (creatures is not null) log($"  Prepared startup state commands: {creatures.StateCommands.Publications.Count} private update messages.");
        if (creatures is not null) log($"  Prepared startup equipment commands: {creatures.EquipmentCommands.Publications.Count} private item messages.");
        if (creatures is not null) log($"  Prepared startup items: {creatures.Items.Created.Count}; complete item messages retained privately.");
        if (context.ScriptRegions is { } regions) log($"  Script-region keys: {regions.Records.Count}; records: {regions.Records.Sum(pair=>pair.Value.Count)}; retained privately for bank 1 section 5.");
        log($"  Known startup parameter words: {context.StartupParameters.Words.Count}; untouched words require native initialization.");
        log($"  Regional replacement baseline: {(context.RegionReplacements is null ? "unknown" : "8,600 bytes from original reset")}; opcode-116 mutations {context.Coverage.GetValueOrDefault(SacredScriptRegionReplacementLayout.SetOpcode)}, complete banks remain pending.");
        if (triggers is not null)
            log($"  World triggers: original bound {triggers.AuthoredTableBound}, runtime slots {triggers.Count}, unknown slots {triggers.UnknownSlotCount}, {triggers.Patches.Count} cell mappings, {triggers.PendingPositions.Count} queued tile positions.");
        if (objects is not null)
            log($"  Constructed objects: {objects.Objects.Count}; object callbacks {objects.Callbacks.Count}; " +
                $"pending animations {objects.Objects.Count(obj => obj.PendingAnimationEvent is not null)}; " +
                "physical placement/bone state and item value initialization required before upload.");
        if (creatures is not null)
            log($"  Prepared startup creatures: {creatures.Prepared.Count}; {creatures.Prepared.Sum(value => value.Publications.Count)} ordered messages retained privately, complete campaign upload pending.");
        if (creatures?.Pending is { } pending)
            log($"  Startup creature: stage {pending.Stage}, ref {pending.Actor?.Base.ObjectReference}; incomplete opcode, no upload credit.");
        return context;
    }
}
