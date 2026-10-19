using Sacred.Assets.GameBin;
using Sacred.Core.GameBin.Scripts;

namespace Sacred.GameplayHost.Scripting;

internal static class WorldScriptSectorHandlers
{
    public static bool Execute(WorldScriptExecutionContext context, SacredScriptCommand command)
    {
        if (command.Opcode == SacredScriptSpawnEquipmentLayout.Opcode)
        {
            context.AddSpawnEquipment(SacredScriptSpawnEquipment.Read(command));
            return true;
        }
        if (command.Opcode != SacredScriptExecutionOpcodes.SetSectorMonsterParameters) return false;
        // Native clears ten argument words for each command, then uses only the first three.
        var values = SacredScriptOperandReader.Read(command)
            .Where(operand => operand.Kind == SacredScriptArgumentKind.Integer)
            .Select(operand => WorldScriptDefinitionHandlers.Integer(context, operand)).ToArray();
        // Native still parses operands when no current sector environment can be resolved.
        context.CurrentSector?.SetMonsterParameters(values.ElementAtOrDefault(0),
            values.ElementAtOrDefault(1), values.ElementAtOrDefault(2));
        return true;
    }
}
