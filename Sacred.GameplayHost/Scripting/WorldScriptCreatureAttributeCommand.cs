using Sacred.Assets.GameBin;
using Sacred.Core.GameBin.Scripts;

namespace Sacred.GameplayHost.Scripting;

/// <summary>Gold 0x48DF30 numeric-resource SetCV startup caller. Target lookup
/// precedes its creature cast; missing or non-creature bindings have no effect.</summary>
public static class WorldScriptCreatureAttributeCommand
{
    public static void Apply(WorldScriptExecutionContext context, SacredScriptCommand command,
        WorldScriptCreatureWorld world, WorldScriptCreatureAssets assets,
        WorldScriptCreatureRuntime runtime, Action<string> log)
    {
        if (!context.IsExecutingStartup || !context.Multiplayer)
            throw new NotSupportedException("SetCV requires its compared multiplayer startup caller.");
        var name = ""; var index = -1; var value = 0;
        foreach (var operand in SacredScriptOperandReader.Read(command))
        {
            if (operand.Kind == SacredScriptArgumentKind.NullTerminatedString) name = operand.Text!;
            else if (operand.Kind == SacredScriptArgumentKind.Integer && operand.Integer is { } number)
            {
                if (index < 0) index = number; else value = number;
            }
            else throw new NotSupportedException("SetCV requires literal resource/index/value operands.");
        }
        if (name.Length == 0) return;
        if (!name.StartsWith("res:", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException("SetCV name/hero target requires its original normalization and lifecycle.");
        var resource = WorldScriptResourceReference.Resolve(name, assets.Equipment?.Naming?.Resources ??
            throw new InvalidDataException("SetCV requires original resource text lookup."));
        if (world.FindObject(resource, context) is not { } reference || !world.Factory.Actors.TryGetValue(reference, out var actor)) return;
        if (actor.Base.Owner != runtime.Owner)
            throw new NotSupportedException("SetCV requires its compared local initial actor.");
        var template = assets.Creatures.Entries.Single(row => row.ItemType == actor.Base.ObjectType);
        var next = WorldCreatureScriptBaseAttribute.Apply(actor, index, value, template, assets.Results,
            assets.Levels, assets.Progression, assets.LevelDefinitions, assets.Equipment!, runtime.Difficulty,
            runtime.Owner, context.Multiplayer, runtime.Players);
        world.UpdateScriptActor(next);
        log($"Startup SetCV: ref {reference}, base attribute {index}, input {value}; current-level results and health private, no publication.");
    }
}
