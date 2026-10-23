using System.Collections.ObjectModel;
using Sacred.Assets.GameBin;
using Sacred.Core.GameBin.Scripts;

namespace Sacred.GameplayHost.Scripting;

/// <summary>One mutable script run, shared by headless and graphical consumers.
/// Failed/cancelled state must be discarded before startup is retried.</summary>
public sealed class WorldScriptExecutionContext
{
    private readonly List<SacredBaseTriggerDefinition> _triggers = [];
    private readonly Dictionary<string, List<int>> _movementCallbacks = new(WorldScriptNameComparer.Instance);
    private readonly Dictionary<ushort, int> _coverage = [];
    private readonly List<SacredScriptSpawnEquipment> _spawnEquipment = [];
    private readonly SacredScriptFunction[] _functions;
    public WorldScriptExecutionContext(WorldScriptDefinitions definitions, IReadOnlyList<SacredScriptFunction> functions,
        ReadOnlyMemory<byte> functionCode, bool multiplayer, Func<int>? random = null, Func<int, int, bool>? positionAllowed = null)
    {
        Definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));
        ArgumentNullException.ThrowIfNull(functions);
        _functions = functions.ToArray();
        Functions = Array.AsReadOnly(_functions);
        FunctionCode = functionCode.ToArray();
        Multiplayer = multiplayer;
        Random = random;
        PositionAllowed = positionAllowed;
        Triggers = _triggers.AsReadOnly();
        Coverage = new ReadOnlyDictionary<ushort, int>(_coverage);
        SpawnEquipment = _spawnEquipment.AsReadOnly();
    }
    public WorldScriptDefinitions Definitions { get; }
    public WorldScriptVariables Variables { get; } = new();
    public WorldScriptVariableGlobals VariableGlobals { get; } = new();
    public WorldScriptRegistrationTable Registrations { get; } = new();
    public WorldScriptStartupParameters StartupParameters { get; } = new();
    public IReadOnlyList<SacredScriptFunction> Functions { get; }
    public ReadOnlyMemory<byte> FunctionCode { get; }
    public bool Multiplayer { get; }
    public Func<int>? Random { get; }
    public Func<int, int, bool>? PositionAllowed { get; }
    /// <summary>Gold function dispatch requires an active quest when FunkStru.Quest is nonnegative.
    /// Missing quest state is an unresolved execution dependency.</summary>
    public Func<int, bool?>? QuestActive { get; init; }
    /// <summary>Creates/stores the native trigger and registers its name using world-trigger grants.
    /// An absent factory is required world work, not a successful declaration.</summary>
    public IWorldScriptTriggerRegistry? WorldTriggers { get; init; }
    public IWorldScriptObjectWorld? ObjectWorld { get; init; }
    /// <summary>A consumer may reject an effect whose required runtime services are unavailable,
    /// before any mutation. Rejection is reported with the same file offset/call path as handler failure.</summary>
    public Action<SacredScriptCommand>? BeforeCommand { get; init; }
    public WorldScriptSectorEnvironment? CurrentSector { get; internal set; }
    public bool IsInitializingSector { get; internal set; }
    public bool IsEnteringSector { get; internal set; }
    public bool IsExecutingStartup { get; internal set; }
    public IReadOnlyList<SacredBaseTriggerDefinition> Triggers { get; }
    public IReadOnlyDictionary<ushort, int> Coverage { get; }
    /// <summary>Ordered opcode 115 rules, cleared at native EnterSector's callback boundary.</summary>
    public IReadOnlyList<SacredScriptSpawnEquipment> SpawnEquipment { get; }
    internal void ClearSpawnEquipment() => _spawnEquipment.Clear();
    internal void AddSpawnEquipment(SacredScriptSpawnEquipment rule) => _spawnEquipment.Add(rule);
    public int ExecutedCommands => _coverage.Values.Sum();
    public bool StartupCompleted { get; internal set; }
    public bool IsInvalidated { get; internal set; }
    public WorldScriptExecutionFailure? Failure { get; internal set; }

    public static WorldScriptExecutionContext Create(WorldCampaignScripts scripts, bool multiplayer, Func<int>? random = null,
        Func<int, int, bool>? positionAllowed = null) =>
        new(WorldScriptDefinitions.Read(File.ReadAllBytes(scripts.Files.DefPosPath)), scripts.Functions, scripts.FunctionCode, multiplayer, random, positionAllowed);

    public IReadOnlyList<int> MovementCallbacks(string name) =>
        _movementCallbacks.TryGetValue(name, out var callbacks) ? callbacks.AsReadOnly() : Array.Empty<int>();
    /// <summary>Native received registration accepts the complete word; invocation validates the eventual function.</summary>
    public void AppendReceivedMovementCallback(string name, uint function)
    {
        if (!_movementCallbacks.TryGetValue(name, out var callbacks)) _movementCallbacks.Add(name, callbacks = []);
        callbacks.Add(unchecked((int)function));
    }
    public void ClearMovementCallbacks() => _movementCallbacks.Clear();
    internal void RegisterMovement(string name, int function)
    {
        RequireFunction(function);
        if (!_movementCallbacks.TryGetValue(name, out var callbacks)) _movementCallbacks.Add(name, callbacks = []);
        callbacks.Add(function);
    }
    internal void AddTrigger(SacredBaseTriggerDefinition trigger) => _triggers.Add(trigger);
    internal void Record(ushort opcode) => _coverage[opcode] = _coverage.GetValueOrDefault(opcode) + 1;
    /// <summary>Replaces one existing definition. Bytecode bounds remain checked at execution time.</summary>
    public void ReplaceFunction(uint index, SacredScriptFunction function)
    {
        if (index < _functions.Length) _functions[index] = function;
    }
    public SacredScriptFunction RequireFunction(int index)
    {
        if ((uint)index >= Functions.Count) throw new InvalidDataException($"Unresolved function index {index}.");
        var function = Functions[index];
        if (function.Start < 0 || function.ByteLength < 0 || (long)function.Start + function.ByteLength > FunctionCode.Length)
            throw new InvalidDataException($"Function '{function.Name}' exceeds FunkCode.bin.");
        return function;
    }
    internal int FindFunction(string name)
    {
        for (var i = 0; i < Functions.Count; i++)
            if (WorldScriptNameComparer.Instance.Equals(Functions[i].Name, name)) return i;
        throw new InvalidDataException($"Unresolved function '{name}'.");
    }
}
