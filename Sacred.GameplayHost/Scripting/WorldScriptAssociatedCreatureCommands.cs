using Sacred.Assets.GameBin;
using Sacred.Core.GameBin.Scripts;

namespace Sacred.GameplayHost.Scripting;

/// <summary>Commands compared with a non-null associated creature. A caller
/// reference cannot silently enter an unbound handler or predicate.</summary>
internal static class WorldScriptAssociatedCreatureCommands
{
    public static void RequireSupported(WorldScriptExecutionContext context, SacredScriptCommand command)
    {
        if (context.AssociatedCreatureReference == 0 || command.Opcode is SacredScriptControlOpcodes.Nop or SacredScriptCreateCreature.Opcode or
            SacredScriptExecutionOpcodes.ActivateQuest or SacredScriptPortalOpcodes.SetBaseTrigger or
            SacredScriptTriggerOpcodes.SetTrigger or SacredScriptTriggerOpcodes.TriggerPatch or SacredScriptExecutionOpcodes.QuestCompassObject) return;
        if (command.Opcode == SacredScriptCommandHeaderLayout.SetCreatureStateOpcode)
        {
            var operands = SacredScriptOperandReader.Read(command);
            if (operands.Count == 2 && operands[0].Kind == SacredScriptArgumentKind.NullTerminatedString &&
                !string.IsNullOrEmpty(operands[0].Text) && operands[1].Kind is
                    SacredScriptArgumentKind.DialogRegistrationName or SacredScriptArgumentKind.DialogRegistrationNumber) return;
        }
        throw new NotSupportedException($"Opcode {command.Opcode} requires its native associated-creature behavior before execution.");
    }
}
