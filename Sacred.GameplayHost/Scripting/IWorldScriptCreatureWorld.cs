using Sacred.Core.GameBin.Scripts;

namespace Sacred.GameplayHost.Scripting;

public interface IWorldScriptCreatureWorld
{
    void Create(WorldScriptExecutionContext context, SacredScriptCommand command);
}
