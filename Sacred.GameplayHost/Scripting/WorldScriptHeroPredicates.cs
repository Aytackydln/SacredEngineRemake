using System.Buffers.Binary;
using System.Text;
using Sacred.Core.GameBin.Scripts;

namespace Sacred.GameplayHost.Scripting;

/// <summary>Compared IsHero branch. Present heroes require their actual owner
/// and original symbolic type resolution; unresolved contexts remain unknown.</summary>
public static class WorldScriptHeroPredicates
{
    public static bool? Evaluate(WorldScriptExecutionContext context,SacredScriptCommand command)
    {
        var data=command.Bytes.Span;
        if (command.Opcode is not (SacredScriptControlOpcodes.If or SacredScriptControlOpcodes.ElseIf) ||
            data.Length<6 || BinaryPrimitives.ReadUInt16LittleEndian(data[2..])!=data.Length || data[4]!=SacredScriptConditionTags.IsHero ||
            data[5..].IndexOf((byte)0)!=data.Length-6) return null;
        var name=Encoding.Latin1.GetString(data[5..^1]);
        if(name.Contains('$') || name.Contains("+Var(",StringComparison.Ordinal) || name.StartsWith("res:",StringComparison.OrdinalIgnoreCase)) return null;
        return context.HasActiveHero switch { false => false, true => context.MatchActiveHero?.Invoke(name), _ => null };
    }
    // Actual IsHero's expansion-compatible alias. This is a native interpreter
    // rule, not an item-specific data repair; exact matching is otherwise retained.
    internal static bool Matches(uint actual, uint resolved) => actual == resolved || actual == 7 && resolved == 6;
}
