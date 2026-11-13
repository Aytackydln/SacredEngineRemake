using Sacred.Assets.GameBin;
using Sacred.Core.GameBin.Scripts;

namespace Sacred.GameplayHost.Scripting;

/// <summary>Compared dispatcher paths that preserve an already established +7 suffix.
/// Unknown handlers and large patch arrays deliberately discard the knowledge.</summary>
internal static class WorldScriptVariableCallerEffects
{
    public static bool Preserves(WorldScriptExecutionContext context, SacredScriptCommand command)
    {
        if (command.Opcode is SacredScriptExecutionOpcodes.SetVariable or
            SacredScriptExecutionOpcodes.DefineVariable or SacredScriptExecutionOpcodes.AddVariable) return true;
        if (!context.IsExecutingStartup) return false;
        if (command.Opcode == SacredScriptRegistrationLayout.Opcode)
            return context.Registrations.AppendPreservesCallerIndex;
        // Mouseover leaves +7 padding intact but changes following position bytes.
        if (command.Opcode == WorldScriptMouseovers.Opcode)
            return context.VariableCallerStopsImmediately &&
                SacredScriptOperandReader.Read(command)
                    .Where(operand => operand.Kind == SacredScriptArgumentKind.ObjectTakeScript)
                    .All(operand => operand.Text!.Length < 128);
        // Gold 0x496FB5 first reaches the following variable suffix in cell 98.
        // Bound the separate concatenated name buffer as well as the cell array.
        return command.Opcode == SacredScriptTriggerOpcodes.TriggerPatch &&
            SacredScriptTriggerPatchReader.Read(command) is { Cells.Count: < 98, Name.Length: < 100 };
    }

    public static void EstablishZero(WorldScriptExecutionContext context)
    {
        context.VariableCallerIndex = 0;
        context.VariableCallerStopsImmediately = true;
    }

    public static void CompleteCreature(WorldScriptExecutionContext context, SacredScriptCommand command)
    {
        // The two-argument NPC caller's cleared 256-byte owner buffer overlaps
        // the subsequent one-argument variable suffix at owner byte 231.
        // Keep explicit owner inputs and possible string expansion/overflow unknown.
        if (context.IsExecutingStartup && SacredScriptOperandReader.Read(command).All(operand =>
                operand.Kind != SacredScriptArgumentKind.CreatureOwner &&
                (operand.Text is null || operand.Text.Length < 128 &&
                    !operand.Text.Contains('+') && !operand.Text.Contains('$'))))
            EstablishZero(context);
    }

    public static bool StopsImmediately(char first) =>
        first is not (>= '0' and <= '9') && first is not ('+' or '-' or ' ') && first is not (>= '\t' and <= '\r');
}
