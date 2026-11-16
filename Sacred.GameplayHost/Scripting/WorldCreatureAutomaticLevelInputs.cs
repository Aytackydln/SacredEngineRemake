namespace Sacred.GameplayHost.Scripting;

/// <summary>One authoritative automatic-level snapshot. Team entries are levels
/// after native registry/cast resolution; null entries represent failed lookups.
/// The caller owns hero identity, team selection, sector readiness and freshness.</summary>
public sealed class WorldCreatureAutomaticLevelInputs(ushort heroLevel,IEnumerable<ushort?> teamLevels,
    uint elapsedCounterMilliseconds,byte progressByte28,byte horseAttribute22,
    bool sectorLoaded,bool sectorPresent,ushort sectorMinimum,ushort sectorMaximum)
{
    public ushort HeroLevel { get; }=heroLevel;
    public IReadOnlyList<ushort?> TeamLevels { get; }=Array.AsReadOnly(teamLevels.ToArray());
    public uint ElapsedCounterMilliseconds { get; }=elapsedCounterMilliseconds;
    public byte ProgressByte28 { get; }=progressByte28;
    public byte HorseAttribute22 { get; }=horseAttribute22;
    public bool SectorLoaded { get; }=sectorLoaded;
    public bool SectorPresent { get; }=sectorPresent;
    public ushort SectorMinimum { get; }=sectorMinimum;
    public ushort SectorMaximum { get; }=sectorMaximum;
}
