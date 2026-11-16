using Sacred.Assets.GameBin;
using Sacred.Core.GameBin.Scripts;

namespace Sacred.GameplayHost.Scripting;

/// <summary>Compared region-wrapper scope: scalar predicates and calls rejected
/// by native entry-name gates. Inspect the whole body before executing anything;
/// an effectful callback remains pending rather than leaving a partial wrapper.</summary>
public static class WorldScriptRegionCallbackProfile
{
    public static bool TryInvoke(WorldScriptExecutionContext context, ushort index,
        CancellationToken token, out string? blocker)
    {
        token.ThrowIfCancellationRequested();
        blocker = null;
        // 0x492880 ignores the native dispatcher's false return.
        if (index == 0 || index >= context.Functions.Count) return true;
        var function = context.Functions[index];
        foreach (var command in SacredCompiledScriptReader.Read(context.FunctionCode.Slice(function.Start, function.ByteLength)))
        {
            if (command.Opcode is SacredScriptControlOpcodes.Stop or SacredScriptControlOpcodes.Nop or SacredScriptControlOpcodes.BranchEnd)
                continue;
            if (command.Opcode is SacredScriptControlOpcodes.If or SacredScriptControlOpcodes.ElseIf &&
                SacredScriptConditionReader.Evaluate(command, context.Multiplayer, context.Variables.Find) is not null)
                continue;
            if (command.Opcode == SacredScriptExecutionOpcodes.CallFunction && IsExcludedCall(context, command))
                continue;
            if (command.Opcode == SacredScriptExecutionOpcodes.DefinePosition ||
                command.Opcode == SacredScriptCreateCreature.Opcode && context.CreatureWorld is WorldScriptCreatureWorld)
                continue;
            if (command.Opcode == SacredScriptExecutionOpcodes.ActivateQuest && WorldScriptRegionQuestActivation.Supports(context, command))
                continue;
            blocker = $"Region callback '{function.Name}' (function {index}) requires native caller services for opcode {command.Opcode} at 0x{function.Start + command.FileOffset:X}; no body executed.";
            return false;
        }
        new WorldScriptExecutor(context).InvokeRegion(index, token);
        return true;
    }

    private static bool IsExcludedCall(WorldScriptExecutionContext context, SacredScriptCommand command)
    {
        var operands = SacredScriptOperandReader.Read(command);
        if (operands.Count != 1 || operands[0].Kind != SacredScriptArgumentKind.NullTerminatedString) return false;
        var name = WorldScriptDefinitionHandlers.Text(operands[0]);
        // Other name shapes first enter object/resource association services.
        if (name.Contains('+') || name.Contains(':')) return false;
        for (var index = 1; index < context.Functions.Count; index++)
        {
            var target = context.Functions[index];
            if (!WorldScriptNameComparer.Instance.Equals(target.Name, name)) continue;
            return Prefix("InitRg") && (target.Name.Contains("Pools", StringComparison.Ordinal) ||
                target.Name.Contains("(DQ_Quest)", StringComparison.Ordinal)) || Prefix("DQTrgRegion") || Prefix("DQStartRegion");
            bool Prefix(string prefix) => target.Name.Length >= prefix.Length &&
                WorldScriptNameComparer.Instance.Equals(target.Name[..prefix.Length], prefix);
        }
        return false;
    }
}
