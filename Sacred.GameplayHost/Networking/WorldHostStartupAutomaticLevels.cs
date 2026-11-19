using System.Buffers.Binary;
using Sacred.Core.World;
using Sacred.GameplayHost.Scripting;
using Sacred.Network.Gameplay;

namespace Sacred.GameplayHost.Networking;

/// <summary>Current entered graph during first-world StartCode/MultiCampaign, before a gameplay
/// tick. Original cold cStats construction clears its elapsed counter; selected
/// CE import affects its separate extension. This is not an advancing statistics
/// service and cannot supply later callbacks/regions after this scope closes.</summary>
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
        var present = sectors.TryGetSector(sector, out var entry);
        return new(hero.Level, levels, 0, actor.Scratch.Length > 28 ? actor.Scratch[28] : (byte)0, skill,
            hero.Terrain.IsSectorLoaded(sector), present,
            present ? entry.Environment.MonsterMinLevel : (ushort)0, present ? entry.Environment.MonsterMaxLevel : (ushort)0);
    }
}
