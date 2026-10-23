using Sacred.Core.GameBin.Scripts;
using Sacred.GameplayHost.Scripting;
using Sacred.Network.Gameplay;

namespace Sacred.GameplayHost.Networking;

/// <summary>Replays received changes between callbacks, preserving definitions authored by earlier local script effects.</summary>
public static class WorldNetworkScriptDefinitions
{
    public static void ApplyPending(GoldScriptDefinitionReplica source, WorldScriptDefinitions definitions)
    {
        ArgumentNullException.ThrowIfNull(source);
        while (source.TryPeekChange(out var change))
        {
            Apply(change!, definitions);
            source.CompleteChange(change!);
        }
    }
    public static void Apply(GoldScriptDefinitionChange change, WorldScriptDefinitions definitions)
    {
        ArgumentNullException.ThrowIfNull(change);
        ArgumentNullException.ThrowIfNull(definitions);
        if (change.Snapshot is { } snapshot)
            definitions.Import(snapshot.Select(row => WorldScriptDefinition.FromPayload(row.ToPayload())));
        else if (change.Event is { } entry)
        {
            var records = definitions.Records.Select(row => SacredScriptDefinition.FromPayload(row.ToPayload())).ToList();
            GoldScriptDefinitionReplica.ApplyTo(records, entry);
            definitions.Import(records.Select(row => WorldScriptDefinition.FromPayload(row.ToPayload())));
        }
    }
}
