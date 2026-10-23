using System.Text;
using Sacred.Core.GameRes;
using Sacred.GameplayHost.Scripting;
using Sacred.Network.Gameplay;

namespace Sacred.GameplayHost.Networking;

/// <summary>Imports callback indices and resolves bank hashes; registering a callback does not invoke it.</summary>
public static class WorldNetworkMovementCallbacks
{
    public static void ApplyPending(GoldMovementCallbackReplica source, WorldScriptExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(context);
        while (source.TryPeekChange(out var change))
        {
            if (change!.Snapshot is { } groups)
            {
                if (groups.Count == 0)
                {
                    context.ClearMovementCallbacks(); source.CompleteChange(change); continue;
                }
                if (change.Functions is null || !change.Functions.TryGetDefinitions(out var definitions)) return;
                var functions = new Dictionary<uint, uint>();
                for (var index = 0; index < definitions.Count; index++)
                    functions.TryAdd(SacredResourceHash.ComputeDefaultCLocale(Encoding.Latin1.GetBytes(definitions[index].Name)), (uint)index);
                var vectors = groups.Select(group => group.Functions.Select(value => value.Value).ToList()).ToArray();
                // The native tree allows duplicate keys. Its post-import traversal looks up the first
                // matching key again for each entry, rather than remapping the traversal node directly.
                foreach (var group in groups)
                {
                    var first = 0;
                    while (!WorldScriptNameComparer.Instance.Equals(groups[first].Name, group.Name)) first++;
                    var vector = vectors[first];
                    if (vector.Count > 1000) vector.Clear();
                    else for (var index = 0; index < vector.Count; index++)
                        vector[index] = functions.GetValueOrDefault(vector[index]);
                }
                context.ClearMovementCallbacks();
                var imported = new HashSet<string>(WorldScriptNameComparer.Instance);
                for (var index = 0; index < groups.Count; index++)
                    if (imported.Add(groups[index].Name))
                        foreach (var function in vectors[index])
                            context.AppendReceivedMovementCallback(groups[index].Name, function);
            }
            else if (change.Registration is { } entry)
                context.AppendReceivedMovementCallback(entry.Name, entry.FunctionIndex);
            source.CompleteChange(change);
        }
    }
}
