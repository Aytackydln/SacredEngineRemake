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
        var context = new WorldScriptExecutionContext(definitions, scripts.Functions, scripts.FunctionCode, multiplayer: true)
        { CreateWorldTrigger = triggers is null ? null : triggers.Create, PatchWorldTrigger = triggers is null ? null : triggers.Patch,
            ObjectWorld = objects };
        log($"Network startup execution: {scripts.Files.Name}; {context.Definitions.Records.Count} authored definitions, {context.Functions.Count} functions loaded.");
        try { new WorldScriptExecutor(context).ExecuteStartup(scripts.StartupCode, cancellationToken); }
        catch (WorldScriptExecutionFailure failure) { log($"Network startup blocked: {failure.Message}"); }
        log($"Network startup state: {context.ExecutedCommands} executed commands; {context.Variables.Records.Count} variables, " +
            $"{context.Definitions.Records.Count} definitions, {context.Triggers.Count} base triggers; complete {context.StartupCompleted}.");
        foreach (var pair in context.Coverage.OrderBy(pair => pair.Key)) log($"  Executed opcode {pair.Key}: {pair.Value}.");
        log($"  Variable global writes: {context.VariableGlobals.Mutations.Count}; unresolved indices {context.VariableGlobals.UnresolvedIndexCount}; original initialization required before application.");
        log($"  Executed 80-byte registrations: {context.Registrations.Count} (includes native reserved entry zero).");
        log($"  Known startup parameter words: {context.StartupParameters.Words.Count}; untouched words require native initialization.");
        if (triggers is not null)
            log($"  World triggers: original bound {triggers.AuthoredTableBound}, runtime slots {triggers.Count}, unknown slots {triggers.UnknownSlotCount}, {triggers.Patches.Count} cell mappings, {triggers.PendingPositions.Count} queued tile positions.");
        if (objects is not null)
            log($"  Constructed objects: {objects.Objects.Count}; object callbacks {objects.Callbacks.Count}; " +
                $"pending animations {objects.Objects.Count(obj => obj.PendingAnimationEvent is not null)}; " +
                "physical placement/bone state and item value initialization required before upload.");
        return context;
    }
}
