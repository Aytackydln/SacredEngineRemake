using Sacred.GameplayHost.Scripting;
using Sacred.Network.Gameplay;

namespace Sacred.GameplayHost.Networking;

/// <summary>Imports received definitions without executing them or modifying authored campaign data.</summary>
public static class WorldNetworkScriptFunctions
{
    public static void Apply(GoldScriptFunctionReplica functions, WorldScriptExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(functions);
        ArgumentNullException.ThrowIfNull(context);
        if (!functions.IsConfigured || functions.Definitions.Count != context.Functions.Count)
            throw new InvalidOperationException("Network function vector must match the selected campaign.");
        foreach (var pair in functions.Updates)
        {
            var row = pair.Value;
            context.ReplaceFunction(pair.Key, new(row.Name, row.Start, row.ByteLength, row.Quest, row.Number, row.Deleted));
        }
    }
}
