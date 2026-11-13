using Sacred.Assets.GameBin;
using Sacred.Core.GameBin.Scripts;
using Sacred.Core.GameRes;
using System.Globalization;
using System.Text;

namespace Sacred.GameplayHost.Scripting;

/// <summary>Gold opcode 64's missing-journal startup branch. Existing journal
/// targets require real refresh/GUI effects and remain guarded before mutation.</summary>
public static class WorldScriptQuestObjectCompass
{
    public static void Apply(WorldScriptExecutionContext context, SacredScriptCommand command)
    {
        if (!context.IsExecutingStartup) throw new NotSupportedException("QuestKompassObj live journal/publication effects are unresolved.");
        ApplyDeferred(context.Quests ?? throw new InvalidDataException("QuestKompassObj requires original quest/journal state."), command,
            name => name.StartsWith("Res:", StringComparison.OrdinalIgnoreCase)
                ? WorldScriptResourceReference.Resolve(name, context.ResourceText ?? throw new InvalidDataException("QuestKompassObj requires original resource text."))
                : World(context).Resources.FindLocalResource(name) ?? throw new NotSupportedException("QuestKompassObj local name requires native registration."),
            resource => World(context).FindObject(resource, context), name => DecodeName(context, name));
    }

    public static void ApplyDeferred(WorldScriptQuests quests, SacredScriptCommand command,
        Func<string, uint> resolveResource, Func<uint, uint?> findObject, Func<string, string>? decodeName = null)
    {
        if (command.Opcode != SacredScriptExecutionOpcodes.QuestCompassObject)
            throw new ArgumentException("Expected QuestKompassObj.", nameof(command));
        string? name = null;
        string? decodedName = null;
        int? number = null;
        var radius = SacredScriptJournalLayout.DefaultTargetRadius;
        foreach (var operand in SacredScriptOperandReader.Read(command))
        {
            if (operand.Kind == SacredScriptArgumentKind.NullTerminatedString)
            {
                var text = operand.Text!;
                if (text.Contains('$') || text.Contains("+Var(", StringComparison.Ordinal))
                    throw new NotSupportedException("QuestKompassObj name/radius expressions require native substitution.");
                var original = text;
                text = decodeName?.Invoke(text) ?? text;
                if (text.StartsWith(SacredScriptJournalLayout.ObjectRadiusPrefix, StringComparison.OrdinalIgnoreCase))
                    radius = unchecked((uint)Atoi(text.AsSpan(SacredScriptJournalLayout.ObjectRadiusPrefix.Length)));
                else { name = original; decodedName = text; }
            }
            else if (operand.Kind == SacredScriptArgumentKind.Integer)
                number = operand.Integer ?? throw new NotSupportedException("QuestKompassObj quest expressions require native resolution.");
        }
        if (string.IsNullOrEmpty(decodedName)) return;
        var resource = resolveResource(name!);
        if (resource == uint.MaxValue) return; // Original -1 selector is the sole excluded Int32 resource.
        if (number is null) throw new NotSupportedException("QuestKompassObj omitted quest requires function quest save/restore.");
        if (quests.Journal.FindQuest(number.Value) >= 0)
            throw new NotSupportedException("QuestKompassObj existing journal requires native object refresh and GUI before mutation.");
        quests.SetPendingObjectCompass(resource, radius, findObject(resource));
    }

    private static WorldScriptCreatureWorld World(WorldScriptExecutionContext context) => context.CreatureWorld as WorldScriptCreatureWorld ??
        throw new NotSupportedException("QuestKompassObj requires the shared original resource/object lookup.");

    private static string DecodeName(WorldScriptExecutionContext context, string operand)
    {
        if (!operand.StartsWith("Res:", StringComparison.OrdinalIgnoreCase)) return operand;
        var text = context.ResourceText ?? throw new InvalidDataException("QuestKompassObj requires original resource text.");
        var resource = WorldScriptResourceReference.Resolve(operand, text);
        var key = (resource & 0x80000000) != 0 ? resource & 0x7fffffff :
            SacredResourceHash.ComputeDefaultCLocale(Encoding.Latin1.GetBytes(resource.ToString(CultureInfo.InvariantCulture)));
        return text.Strings.TryGetValue(key, out var resolved) ? resolved : operand;
    }

    private static int Atoi(ReadOnlySpan<char> text)
    {
        var offset = 0;
        while (offset < text.Length && (text[offset] == ' ' || text[offset] is >= '\t' and <= '\r')) offset++;
        var negative = offset < text.Length && text[offset] == '-';
        if (offset < text.Length && text[offset] is '+' or '-') offset++;
        var value = 0;
        while (offset < text.Length && text[offset] is >= '0' and <= '9') value = unchecked(value * 10 + text[offset++] - '0');
        return negative ? unchecked(-value) : value;
    }
}
