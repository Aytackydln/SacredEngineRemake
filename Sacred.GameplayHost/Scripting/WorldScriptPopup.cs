using Sacred.Assets.GameBin;
using Sacred.Core.GameBin.Scripts;

namespace Sacred.GameplayHost.Scripting;

/// <summary>Gold 0x4A7760 literal dialog-registration lookup. An absent active
/// hero ends the command before dialog/authority/UI effects, including during startup.</summary>
public static class WorldScriptPopup
{
    public static void Apply(WorldScriptExecutionContext context, SacredScriptCommand command)
    {
        var name = "";
        foreach (var operand in SacredScriptOperandReader.Read(command))
        {
            if (operand.Kind != SacredScriptArgumentKind.NullTerminatedString)
                throw new NotSupportedException("POPUP requires its compared literal registration name.");
            name = operand.Text!;
            if (name.Contains('$') || name.Contains("+Var(", StringComparison.Ordinal) || name.StartsWith("res:", StringComparison.OrdinalIgnoreCase))
                throw new NotSupportedException("POPUP registration requires native string normalization.");
        }
        var selected = name.Length == 0 ? -1 : WorldCreatureScriptDialog.Resolve(context.Registrations, new(name, null));
        if (selected < 0) return;
        if (context.HasActiveHero == true && context.EnterHeroPopup is { } enter)
        {
            enter(context, selected);
            return;
        }
        if (context.HasActiveHero != false)
            throw new NotSupportedException("POPUP requires native hero dialog/authority/UI lifecycle outside the compared absent-hero path.");
    }
}
