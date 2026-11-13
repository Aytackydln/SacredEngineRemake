using Sacred.Assets.GameBin;
using Sacred.Core.GameBin.Scripts;
using Sacred.Network.Gameplay;

namespace Sacred.GameplayHost.Scripting;

/// <summary>Compared SetObjState literal-name miss. Found objects require their
/// native trigger/door/physical branch before they may be changed.</summary>
public sealed class WorldScriptObjectStateCommands(WorldScriptCreatureWorld world,Action<string> log)
{
    public void SetObjectState(WorldScriptExecutionContext context,SacredScriptCommand command)
    {
        if (!context.IsExecutingStartup) throw new NotSupportedException("Live SetObjState requires its original target lifecycle.");
        var change=SacredScriptObjectStateReader.Read(command);
        if (string.IsNullOrEmpty(change.Name)) return;
        if (change.Name.Contains('$') || change.Name.StartsWith("Res:",StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException("SetObjState needs original expression/resource-name operand resolution.");
        var resource=world.Resources.FindLocalResource(change.Name);
        if (resource is { } id && id!=GoldScriptResourceLayout.UnnamedResource && world.FindObject(id,context) is not null)
            throw new NotSupportedException("SetObjState needs its native found-object trigger/door effects.");
        var added=context.DeferredObjectStates.AppendMissing(change);
        log($"Startup SetObjState: missing '{change.Name}', open {change.Open}, lock {change.Locked}; deferred {(added ? "appended" : "unchanged")}, {context.DeferredObjectStates.Records.Count} rows; replay pending.");
    }
}
