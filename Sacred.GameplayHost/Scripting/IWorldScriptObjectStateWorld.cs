using Sacred.Core.GameBin.Scripts;
namespace Sacred.GameplayHost.Scripting;
public interface IWorldScriptObjectStateWorld
{
    void SetObjectState(WorldScriptExecutionContext context,SacredScriptCommand command);
}
