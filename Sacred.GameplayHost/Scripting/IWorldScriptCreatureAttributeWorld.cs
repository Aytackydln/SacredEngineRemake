using Sacred.Core.GameBin.Scripts;

namespace Sacred.GameplayHost.Scripting;

public interface IWorldScriptCreatureAttributeWorld
{
    void SetBaseAttribute(WorldScriptExecutionContext context, SacredScriptCommand command);
}
