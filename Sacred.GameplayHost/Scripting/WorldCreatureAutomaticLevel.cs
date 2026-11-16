using Sacred.Core.GameBin.Balance;
using Sacred.Core.Runtime;
using Sacred.GameplayHost.Runtime;

namespace Sacred.GameplayHost.Scripting;

/// <summary>RequestedLevel retains the UInt16 actually consumed by SetLevel;
/// the native Horse call can leave unrelated high argument bits on the stack.</summary>
public readonly record struct WorldCreatureAutomaticLevelSelection(int RequestedLevel,int BaseLevel,uint ExtraPercent);

/// <summary>Gold 0x48440E..0x48475B before SetLevel. Actor construction, query
/// provenance and the final SetLevel calculation remain separate responsibilities.</summary>
public static class WorldCreatureAutomaticLevel
{
    public static WorldCreatureAutomaticLevelSelection Select(WorldCreatureAutomaticLevelInputs inputs,uint difficulty,
        bool multiplayer,bool horse,SacredCreatureLevelBalance levels,SacredCreatureBalance progression,
        SacredCreatureAutomaticLevelTuning tuning,SacredX87Precision precision,Func<int> random)
    {
        ArgumentNullException.ThrowIfNull(inputs);ArgumentNullException.ThrowIfNull(random);
        if(precision==SacredX87Precision.Extended)
            throw new NotSupportedException("Automatic requested-level selection requires compared startup x87 precision.");
        var index=SacredCreatureProgressionRules.DifficultyIndex(difficulty);
        var level=(int)inputs.HeroLevel;
        if(multiplayer)
            foreach(var member in inputs.TeamLevels)
                if(member is { } memberLevel&&memberLevel>level)level=memberLevel;
        if(inputs.SectorLoaded&&inputs.SectorPresent&&inputs.SectorMinimum!=0&&inputs.SectorMaximum!=0)
        {
            var minimum=unchecked((int)(levels.InputAdditions[index]+inputs.SectorMinimum));
            var maximum=unchecked((int)(levels.MaximumLevels[index]+inputs.SectorMaximum));
            if(level<minimum)level=minimum;
            else if(level>maximum)level=maximum;
            else level=unchecked(level+random()%2);
        }
        var extra=SacredCreatureAutomaticLevelMath.ExtraPercent(inputs.ElapsedCounterMilliseconds,inputs.ProgressByte28,difficulty,tuning,precision);
        var request=horse?unchecked(level+(inputs.HorseAttribute22>>3)):
            unchecked((int)SacredCreatureProgressionRules.NormalizeLevel(unchecked((uint)level),difficulty,progression,precision,extra));
        return new(unchecked((ushort)request),level,extra);
    }
}
