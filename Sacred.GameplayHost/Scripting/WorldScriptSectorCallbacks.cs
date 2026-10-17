using System.Globalization;

namespace Sacred.GameplayHost.Scripting;

/// <summary>The script callback portion of Gold initSector/enterSector. Region work, spawning,
/// tile patches and enter notifications are separate required operations.</summary>
public sealed class WorldScriptSectorCallbacks(WorldScriptExecutionContext context, WorldScriptExecutor executor)
{
    public bool Initialize(WorldScriptSectorEnvironment sector, CancellationToken cancellationToken = default) =>
        Invoke(sector, true, cancellationToken);
    public bool Enter(WorldScriptSectorEnvironment sector, CancellationToken cancellationToken = default) =>
        Invoke(sector, false, cancellationToken);

    private bool Invoke(WorldScriptSectorEnvironment sector, bool initialize, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sector);
        if (context.CurrentSector is not null) throw new InvalidOperationException("A sector callback is already executing.");
        context.CurrentSector = sector;
        context.IsInitializingSector = initialize;
        context.IsEnteringSector = !initialize;
        try
        {
            var name = string.Create(CultureInfo.InvariantCulture,
                $"Sector{sector.Coordinate.X}{sector.Coordinate.Y:D3}{(initialize ? "Init" : "Enter")}");
            return executor.InvokeOptional(name, cancellationToken);
        }
        finally
        {
            context.CurrentSector = null;
            context.IsInitializingSector = false;
            context.IsEnteringSector = false;
        }
    }
}
