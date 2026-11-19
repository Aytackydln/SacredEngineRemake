using System.Globalization;
using Sacred.Assets.GameBin;
using Sacred.Core.GameBin.Scripts;

namespace Sacred.GameplayHost.Scripting;

/// <summary>493680's sector callback selection and temporary scope, after its
/// separate 492880 region call. Cached selection uses only the low word.
/// Sector Enter/spawning and final world flags are separate obligations.</summary>
internal static class WorldScriptSectorInitialization
{
    public static (ushort? Function, bool NativeResult) Select(WorldScriptSectorEnvironment sector,
        IReadOnlyList<SacredScriptFunction> functions, Func<string,uint?> cached)
    {
        var name = string.Create(CultureInfo.InvariantCulture,$"Sector{sector.Coordinate.X}{sector.Coordinate.Y:D3}Init");
        if (cached(name) is { } packed) return ((ushort)packed,true);
        var index = -1;
        for (var candidate = 0; candidate < functions.Count; candidate++)
            if (WorldScriptNameComparer.Instance.Equals(functions[candidate].Name,name)) { index = candidate; break; }
        return index == 0 ? (null,false) : (unchecked((ushort)index),true);
    }

    public static bool? TryInvoke(WorldScriptExecutionContext context, WorldScriptSectorEnvironment sector,
        Func<string,uint?> cached, Func<ushort,bool> invoke)
    {
        if (context.IsInvalidated || !context.StartupCompleted || context.CurrentSector is not null ||
            context.IsExecutingStartup || context.AssociatedCreatureReference != 0)
            throw new InvalidOperationException("Sector initialization requires its completed startup and unassociated ordinary scope.");
        var selection = Select(sector,context.Functions,cached);
        context.CurrentSector = sector;
        context.IsInitializingSector = true;
        try { return selection.Function is { } index && !invoke(index) ? null : selection.NativeResult; }
        finally { context.CurrentSector = null; context.IsInitializingSector = false; }
    }
}
