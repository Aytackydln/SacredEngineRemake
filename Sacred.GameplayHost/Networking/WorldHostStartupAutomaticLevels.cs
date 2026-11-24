using System.Buffers.Binary;
using Sacred.Core.World;
using Sacred.GameplayHost.Scripting;
using Sacred.Network.Gameplay;

namespace Sacred.GameplayHost.Networking;

/// <summary>Current entered graph during first-world StartCode/MultiCampaign, before a gameplay
/// tick. Original cold cStats construction clears its elapsed counter; selected
/// CE import affects its separate extension. This is not an advancing statistics
/// service. Initial sector entry reads the same cold statistic and current
/// hero/team, with the environment from the actual sector callback.</summary>
internal sealed class WorldHostStartupAutomaticLevels(WorldHostActiveHeroBinding hero, GoldTeamReplica teams, SacredSectorIndex sectors)
{
    private bool _active = true;
    private bool _campaignOpened;
    public void EndStartup() => _active = false;
    public IDisposable BeginCampaign()
    {
        if (_active || _campaignOpened) throw new InvalidOperationException("Cold campaign inputs require the single post-StartCode loader continuation.");
        hero.RequireCurrent();
        _campaignOpened = true; _active = true;
        return new CampaignScope(this);
    }
    private sealed class CampaignScope(WorldHostStartupAutomaticLevels owner) : IDisposable
    {
        public void Dispose() => owner.EndStartup();
    }

    public WorldCreatureAutomaticLevelInputs Read(ushort sector)
    {
        if (!_active) throw new NotSupportedException("Automatic NPC levels outside the first-world loader require the current statistics/tick lifecycle.");
        var present = sectors.TryGetSector(sector, out var entry);
        return ReadCurrent(sector, present, present ? entry.Environment.MonsterMinLevel : (ushort)0,
            present ? entry.Environment.MonsterMaxLevel : (ushort)0);
    }

    public WorldCreatureAutomaticLevelInputs ReadEntered(WorldScriptSectorEnvironment environment)
    {
        if (_active || !_campaignOpened || !hero.Terrain.IsSectorLoaded(environment.Sector))
            throw new NotSupportedException("Entered NPC levels need the first-world campaign and its current loaded sector.");
        return ReadCurrent(environment.Sector, true, environment.Value.MonsterMinLevel, environment.Value.MonsterMaxLevel);
    }

    private WorldCreatureAutomaticLevelInputs ReadCurrent(ushort sector, bool present, ushort minimum, ushort maximum)
    {
        var actor = hero.Actor;
        var team = teams.GetTeam(actor.Base.ObjectReference);
        var levels = new List<ushort?>();
        if (team != 0)
            for (uint slot = 1; slot <= 16; slot++)
                if (teams.GetTeam(slot) == team)
                    levels.Add(hero.Creatures.TryGetValue(slot, out var member) && member.Node.Active
                        ? BinaryPrimitives.ReadUInt16LittleEndian(member.Component.Values.AsSpan(78)) : null);
        var values = actor.Component.Values;
        byte skill = 0;
        for (var index = 0; index < 8; index++)
            if (values[28 + index] == 22) { skill = (byte)Math.Min(255, values[36 + index] + values[44 + index]); break; }
        return new(hero.Level, levels, 0, actor.Scratch.Length > 28 ? actor.Scratch[28] : (byte)0, skill,
            hero.Terrain.IsSectorLoaded(sector), present, minimum, maximum);
    }
}
