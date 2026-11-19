using Sacred.Core.GameBin.Scripts;
using Sacred.Core.Pak.Creature;
using Sacred.Core.Runtime;

namespace Sacred.GameplayHost.Scripting;

/// <summary>Integrates the compared automatic caller at its original post-service
/// stage. Required authoritative inputs are checked before actor allocation.</summary>
internal static class WorldCreatureAutomaticScriptLevel
{
    public static void RequireInputs(WorldScriptExecutionContext context,SacredScriptCreateCreature creation,
        WorldScriptCreatureRuntime runtime,WorldScriptCreatureAssets assets)
    {
        if(creation.LevelInput!=0)return;
        if(context.AssociatedCreatureReference!=0&&context.HasActiveHero is null)
            throw new NotSupportedException("CreateNPC automatic level requires an established active-hero profile.");
        if(context.HasActiveHero==true&&(runtime.AutomaticLevelInputs is null||assets.AutomaticLevelDefinitions is null||
            assets.Results.StartupPrecision==SacredX87Precision.Extended))
            throw new NotSupportedException("CreateNPC automatic level requires original tuning and current hero/team/sector inputs.");
    }

    public static WorldCreatureInitialActor Apply(WorldCreatureInitialActor actor,WorldScriptCreatureBlueprint blueprint,
        WorldScriptExecutionContext context,WorldScriptCreatureRuntime runtime,WorldScriptCreatureAssets assets,
        ushort sectorMaximum,SacredRandomSequence random,Action<string> log)
    {
        if(blueprint.Creation.LevelInput!=0||context.HasActiveHero!=true)
            return WorldCreatureScriptLevel.ApplyRequested(actor,blueprint.Template,blueprint.Creation.LevelInput,
                assets.Results,assets.Levels,assets.Progression,assets.LevelDefinitions,runtime.Difficulty,runtime.Owner,
                context.Multiplayer,sectorMaximum,runtime.RequirePlayerCount());
        var inputs=runtime.AutomaticLevelInputs!(actor.Base.Sector)??throw new InvalidDataException("Automatic level provider returned no current snapshot.");
        var selected=WorldCreatureAutomaticLevel.Select(inputs,runtime.Difficulty,context.Multiplayer,
            blueprint.Template.Class==SacredCreatureClass.Horse,assets.Levels,assets.Progression,
            assets.AutomaticLevelDefinitions!.Tuning,assets.Results.StartupPrecision,random.Next);
        log($"Startup NPC automatic level: ref {actor.Base.ObjectReference}, hero {inputs.HeroLevel}, sector {actor.Base.Sector}, base {selected.BaseLevel}, extra {selected.ExtraPercent}, request {selected.RequestedLevel}.");
        return WorldCreatureScriptLevel.ApplyAutomaticRequested(actor,blueprint.Template,checked((ushort)selected.RequestedLevel),
            assets.Results,assets.Levels,assets.Progression,assets.LevelDefinitions,runtime.Difficulty,runtime.Owner,
            context.Multiplayer,inputs.SectorPresent?inputs.SectorMaximum:(ushort)0,runtime.RequirePlayerCount());
    }
}
