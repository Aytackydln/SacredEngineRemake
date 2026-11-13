namespace Sacred.GameplayHost.Scripting;

/// <summary>Compared 0x46BA90 return for an empty function, including native
/// exclusions and quest/deleted gates. Nonempty predicate-return execution must
/// supply its own proven dispatcher result; ordinary void callbacks are separate.</summary>
public static class WorldScriptEmptyFunctionResult
{
    public static bool Invoke(WorldScriptExecutionContext context,int index,Action<int> invoke)
    {
        if (index == 0 || (uint)index >= context.Functions.Count) return false;
        var function = context.Functions[index];
        if (unchecked((byte)function.Deleted) != 0 ||
            Prefix("InitRg") && (function.Name.Contains("Pools",StringComparison.Ordinal) || function.Name.Contains("_DQ_Quest_",StringComparison.Ordinal)) ||
            Prefix("DQTrgRegion") || Prefix("DQStartRegion")) return false;
        if (function.Quest >= 0)
        {
            var active = context.QuestActive?.Invoke(function.Quest) ?? context.Quests?.IsActive(function.Quest);
            if (active is null) throw new InvalidDataException("Function return requires established quest state.");
            if (!active.Value) return false;
        }
        if (function.ByteLength != 0) throw new NotSupportedException("Quest predicate needs its native nonempty-function return behavior.");
        invoke(index);
        return true;
        bool Prefix(string value) => function.Name.Length >= value.Length &&
            WorldScriptNameComparer.Instance.Equals(function.Name[..value.Length],value);
    }
}
