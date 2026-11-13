using System.Buffers.Binary;
using System.Text;
using Sacred.Core.GameBin.Scripts;

namespace Sacred.GameplayHost.Scripting;

/// <summary>Compared absent-active-hero IsHero branch. Present heroes require
/// original type-name resolution and native matching, including its alias rule.</summary>
public static class WorldScriptHeroPredicates
{
    public static bool? Evaluate(WorldScriptExecutionContext context,SacredScriptCommand command)
    {
        var data=command.Bytes.Span;
        if (context.HasActiveHero!=false || command.Opcode is not (SacredScriptControlOpcodes.If or SacredScriptControlOpcodes.ElseIf) ||
            data.Length<6 || BinaryPrimitives.ReadUInt16LittleEndian(data[2..])!=data.Length || data[4]!=SacredScriptConditionTags.IsHero ||
            data[5..].IndexOf((byte)0)!=data.Length-6) return null;
        var name=Encoding.Latin1.GetString(data[5..^1]);
        if(name.Contains('$') || name.Contains("+Var(",StringComparison.Ordinal) || name.StartsWith("res:",StringComparison.OrdinalIgnoreCase)) return null;
        return false;
    }
}
