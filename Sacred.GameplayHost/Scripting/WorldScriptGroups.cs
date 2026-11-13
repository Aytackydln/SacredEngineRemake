using System.Collections.ObjectModel;

namespace Sacred.GameplayHost.Scripting;

/// <summary>Initial interpreter group membership (native +0x7510). CreateNPC
/// 0x483A32..0x483A51 appends without duplicates, then stores actor +0x249.
/// Startup sets interpreter +0x14 before dispatch (0x470AC1), suppressing 0x18F
/// while retaining map mutations for the later bank. Live publication is separate.</summary>
public sealed class WorldScriptGroups
{
    private readonly Dictionary<uint, List<uint>> _members = [];
    private readonly Dictionary<uint, IReadOnlyList<uint>> _views = [];

    public WorldScriptGroups() => Groups = new ReadOnlyDictionary<uint, IReadOnlyList<uint>>(_views);
    public IReadOnlyDictionary<uint, IReadOnlyList<uint>> Groups { get; }

    public WorldCreatureInitialActor AssignInitial(WorldCreatureInitialActor actor, int input)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (input == 0) return actor;
        var key = unchecked((uint)input);
        if (!_members.TryGetValue(key, out var members))
        {
            members = [];
            _members.Add(key, members);
            _views.Add(key, members.AsReadOnly());
        }
        if (!members.Contains(actor.Base.ObjectReference)) members.Add(actor.Base.ObjectReference);
        return actor with { ScriptGroup = key };
    }
}
