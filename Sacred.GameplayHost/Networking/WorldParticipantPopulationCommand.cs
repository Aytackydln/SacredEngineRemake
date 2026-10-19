using System.Diagnostics;
using Sacred.Assets.Executable;
using Sacred.Assets.GameBin;
using Sacred.Assets.GameBin.Balance;
using Sacred.Core.GameBin.Scripts;
using Sacred.Core.World.Sector;
using Sacred.GameplayHost.Scripting;

namespace Sacred.GameplayHost.Networking;

/// <summary>FillRegion's empty-list participant path. It executes the native timer
/// and clears the session's pending spawn schedule; any surviving weighted entry
/// still requires the full placement/equipment factory.</summary>
public sealed class WorldParticipantPopulationCommand(SacredPopulationDefinitions? definitions,
    SacredPopulationBalance? balance)
{
    private readonly HashSet<ushort> _weatherWithoutGroundFog = [];
    public Func<uint> ClockMilliseconds { get; init; } = () => unchecked((uint)
        ((Int128)Stopwatch.GetTimestamp() * 1000 / Stopwatch.Frequency));
    public int ExecutedCommands { get; private set; }
    public int ClearedSchedules { get; private set; }

    public void Execute(WorldScriptExecutionContext context, SacredScriptCommand command, uint connectionSetting)
    {
        var input = SacredScriptPopulationReader.Read(command);
        if (connectionSetting != 1 || definitions is null || input.Types.Any(row => !definitions.Excluded(row.Type)))
            throw new InvalidDataException("FillRegion has unfiltered weighted types; native population placement/equipment services are required.");
        var sector = context.CurrentSector ?? throw new InvalidDataException("FillRegion requires a current sector.");
        if (input.SectorX is { } x && (x != sector.Coordinate.X || input.SectorY != sector.Coordinate.Y))
            throw new InvalidDataException("FillRegion targets another sector; its loaded environment/schedule is required.");
        if (balance is null) throw new InvalidDataException("FillRegion requires original Balance.bin timing.");
        // 0x622E20 remembers a failed ground-fog search in local sector flag 0x8000.
        var environment = sector.Value;
        var groundFog = false;
        if (!_weatherWithoutGroundFog.Contains(sector.Sector))
        {
            for (var i = 0; i < 4; i++)
                groundFog |= ((byte)environment.WeatherValid & 1 << i) != 0 && environment.Weather[i].Type == SectorWeatherKind.GroundFog;
            if (!groundFog) _weatherWithoutGroundFog.Add(sector.Sector);
        }
        if (context.IsEnteringSector)
        {
            var threshold = environment.PopulationTimestampMilliseconds / 1000 + (groundFog ? 1u : 0u) == 0
                ? balance.FirstThresholdSeconds : balance.SubsequentThresholdSeconds;
            var seconds = ClockMilliseconds() / 1000;
            if (unchecked((uint)(int)threshold) >= seconds) { ExecutedCommands++; return; }
            sector.SetPopulationTimestamp(ClockMilliseconds());
        }
        // The native local schedule (+0x5A0) starts empty on load, and this path
        // clears it before the zero-weight guard. It cannot enqueue any spawns.
        ClearedSchedules++;
        ExecutedCommands++;
    }
}
