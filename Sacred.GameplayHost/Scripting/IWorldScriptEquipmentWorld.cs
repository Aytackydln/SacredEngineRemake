using Sacred.Core.GameBin.Scripts;
namespace Sacred.GameplayHost.Scripting;
public interface IWorldScriptEquipmentWorld
{
    void Equip(WorldScriptExecutionContext context, SacredScriptCommand command);
}
