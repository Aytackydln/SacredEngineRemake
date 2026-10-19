using Sacred.Assets.Executable;
using Sacred.GameplayHost.Scripting;
using Sacred.Network.Gameplay;

namespace Sacred.GameplayHost.Networking;

/// <summary>Non-host EnterSector prefix: participant region gate, ordered callback,
/// then explicit population dependency. Terrain/population waits never repeat the callback.</summary>
internal sealed class WorldParticipantSectorEntry(WorldScriptExecutionContext context,
    WorldScriptSectorCallbacks callbacks, SacredRegionDefinitions? regions, Action<string> log)
{
    private long _work;
    private bool _callbackCompleted;
    private bool _populationCompleted;
    public WorldParticipantRegionRequirement? PendingRegion { get; private set; }
    public WorldParticipantSectorPopulation? PendingPopulation { get; private set; }
    public Func<WorldParticipantSectorPopulation, bool>? PopulateSector { get; set; }

    public bool TryExecute(GoldSectorWork work, WorldScriptSectorEnvironment sector,
        GoldWorldBankSnapshot bank, CancellationToken cancellationToken)
    {
        if (_work != work.Id)
        {
            Clear();
            _work = work.Id;
        }
        if (!_callbackCompleted)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var region = regions?.ScriptRegion(sector.Value.Region) ??
                throw new InvalidDataException("Native participant EnterSector requires the original script-region mapping.");
            // Gold enterRegion 0x492CED..0x492D13 returns for an ordinary
            // multiplayer participant, except for script regions 20, 21 and 26.
            // Those exceptions still need the received region lifecycle map and
            // native active-hero selection; do not treat raw region flags as it.
            if (region is 20 or 21 or 26)
            {
                var requirement = new WorldParticipantRegionRequirement(sector.Sector, sector.Value.Region, region) { IsEntering = true };
                if (PendingRegion != requirement) log("Network sector entry pending: " + requirement + ".");
                PendingRegion = requirement;
                return false;
            }
            PendingRegion = null;
            var invoked = callbacks.Enter(sector, cancellationToken);
            _callbackCompleted = true;
            log($"Network sector enter callback: {sector.Sector}, {(invoked ? "executed" : "absent")}; {context.SpawnEquipment.Count} spawn-equipment rules.");
            PendingPopulation = WorldParticipantSectorPopulation.Read(sector, bank, context.SpawnEquipment);
        }
        if (!_populationCompleted)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var population = PendingPopulation!;
            if (population.RequiresExecution && PopulateSector?.Invoke(population) != true) return false;
            _populationCompleted = true;
            PendingPopulation = null;
        }
        return true;
    }

    public void Clear()
    {
        _work = 0;
        _callbackCompleted = _populationCompleted = false;
        PendingRegion = null;
        PendingPopulation = null;
    }
}
