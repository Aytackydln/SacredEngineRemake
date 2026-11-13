using Sacred.Core.GameBin.Scripts;
namespace Sacred.GameplayHost.Scripting;
public interface IWorldScriptCreatureStateWorld
{
    void SetState(WorldScriptExecutionContext context, SacredScriptCommand command);
}
