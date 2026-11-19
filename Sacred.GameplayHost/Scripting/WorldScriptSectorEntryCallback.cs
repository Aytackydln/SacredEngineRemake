using System.Globalization;

namespace Sacred.GameplayHost.Scripting;

/// <summary>4939F0's callback scope only. Post-callback area population,
/// saved objects and final patches remain required before sector completion.</summary>
internal static class WorldScriptSectorEntryCallback
{
    public static ushort? Select(WorldScriptExecutionContext context, WorldScriptSectorEnvironment sector, Func<string, uint?> cached)
    {
        var name = string.Create(CultureInfo.InvariantCulture, $"Sector{sector.Coordinate.X}{sector.Coordinate.Y:D3}Enter");
        if (cached(name) is { } packed) return (ushort)packed;
        var index = -1;
        for (var candidate = 0; candidate < context.Functions.Count; candidate++)
            if (WorldScriptNameComparer.Instance.Equals(context.Functions[candidate].Name, name)) { index = candidate; break; }
        return index == 0 ? null : unchecked((ushort)index);
    }

    public static bool TryInvoke(WorldScriptExecutionContext context, WorldScriptSectorEnvironment sector,
        uint connectionSetting, ushort? index, Func<ushort, bool> invoke)
    {
        if (!context.StartupCompleted || context.IsInvalidated || context.CurrentSector is not null ||
            context.AssociatedCreatureReference != 0 || context.IsExecutingStartup)
            throw new InvalidOperationException("Sector entry requires valid completed startup and its unassociated ordinary caller.");
        var previousSetting = context.LocalNetworkSpeedSettings;
        context.LocalNetworkSpeedSettings = connectionSetting;
        context.CurrentSector = sector;
        context.IsEnteringSector = true;
        context.ClearSpawnEquipment();
        try { return index is not { } selected || invoke(selected); }
        finally
        {
            context.CurrentSector = null;
            context.IsEnteringSector = false;
            context.LocalNetworkSpeedSettings = previousSetting;
        }
    }
}
