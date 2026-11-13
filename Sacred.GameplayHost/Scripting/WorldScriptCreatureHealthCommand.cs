using Sacred.Assets.GameBin;
using Sacred.Core.GameBin.Scripts;

namespace Sacred.GameplayHost.Scripting;

/// <summary>Gold 0x48E280 explicit-resource SetHP for a local initial creature.</summary>
public static class WorldScriptCreatureHealthCommand
{
    public static void Apply(WorldScriptExecutionContext context, SacredScriptCommand command,
        WorldScriptCreatureWorld world, WorldScriptCreatureAssets assets,
        WorldScriptCreatureRuntime runtime, Action<string> log)
    {
        if (!context.IsExecutingStartup || !context.Multiplayer)
            throw new NotSupportedException("SetHP requires its compared multiplayer startup caller.");
        var name = ""; var percentage = 0;
        foreach (var operand in SacredScriptOperandReader.Read(command))
        {
            if (operand.Kind == SacredScriptArgumentKind.NullTerminatedString) name = operand.Text!;
            else if (operand.Kind == SacredScriptArgumentKind.Integer && operand.Integer is { } number) percentage = number;
            else throw new NotSupportedException("SetHP requires literal resource and percentage operands.");
        }
        if (!name.StartsWith("res:", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException("SetHP hero/name/implicit target requires its native target lifecycle.");
        var resource = WorldScriptResourceReference.Resolve(name, assets.Equipment?.Naming?.Resources ??
            throw new InvalidDataException("SetHP requires original resource text lookup."));
        if (world.FindObject(resource, context) is not { } reference || !world.Factory.Actors.TryGetValue(reference, out var actor))
        {
            log($"Startup SetHP: resource {resource} has no creature target; native lookup exit.");
            return;
        }
        if (actor.Base.Owner != runtime.Owner)
            throw new NotSupportedException("SetHP foreign ownership requires native authority transfer.");
        world.UpdateScriptActor(WorldCreatureScriptHitPoints.Apply(actor, percentage));
        log($"Startup SetHP: ref {reference}, percentage {percentage}; current health private, no death dispatch or publication.");
    }
}
