using Sacred.Core.GameBin.Scripts;

namespace Sacred.GameplayHost.Scripting;

public interface IWorldScriptCreatureHealthWorld
{
    void SetHitPoints(WorldScriptExecutionContext context, SacredScriptCommand command);
}
