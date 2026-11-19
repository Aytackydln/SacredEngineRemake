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
    private readonly List<WorldScriptLocalGuiRequest> _localGuiRequests = [];
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
        LocalGuiRequests = _localGuiRequests.AsReadOnly();
        PostStartup = new(this);
    }
    public WorldScriptDefinitions Definitions { get; }
    public WorldScriptVariables Variables { get; } = new();
    public WorldScriptVariableGlobals VariableGlobals { get; init; } = new();
    // Only established suffix effects within one dispatcher frame. Unknown handlers invalidate it.
    private int? _variableCallerIndex;
    internal int? VariableCallerIndex
    {
        get => _variableCallerIndex;
        set { _variableCallerIndex = value; if (value is null) VariableCallerStopsImmediately = false; }
    }
    // Index zero alone is insufficient when a later caller changes bytes after +7.
    internal bool VariableCallerStopsImmediately { get; set; }
    public WorldScriptRegistrationTable Registrations { get; } = new();
    public WorldScriptDialogAssociations DialogAssociations { get; } = new();
    public WorldScriptEquipmentPools EquipmentPools { get; init; } = new();
    /// <summary>Original 0x470B11 reload after StartCode. Null models an absent
    /// file/open failure, which preserves the current pools.</summary>
    public SacredScriptEquipmentPoolArchive? EquipmentPoolReload { get; init; }
    public WorldScriptDeferredObjectStates DeferredObjectStates { get; } = new();
    public WorldScriptObjectCallbacks ObjectCallbacks { get; } = new();
    public WorldScriptGroups Groups { get; } = new();
    public WorldScriptStartupParameters StartupParameters { get; init; } = new();
    /// <summary>Explicit reset/restored regional replacement baseline. Supported
    /// startup has no opcode-116 mutations; live actor transformation is separate.</summary>
    public SacredScriptRegionReplacementTable? RegionReplacements { get; init; }
    /// <summary>Only bank 1's second fixed block. A failed/incomplete run or an
    /// unimplemented mutation cannot publish the unchanged baseline as final state.</summary>
    public byte[] WriteRegionReplacementBankBlock()
    {
        if (!StartupCompleted || IsInvalidated || Coverage.ContainsKey(SacredScriptRegionReplacementLayout.SetOpcode))
            throw new InvalidDataException("Regional replacement output requires completed valid startup without unresolved opcode-116 effects.");
        return (RegionReplacements ?? throw new InvalidDataException("Regional replacement output requires original reset or restored provenance.")).ToPayload();
    }
    public IReadOnlyList<SacredScriptFunction> Functions { get; }
    public ReadOnlyMemory<byte> FunctionCode { get; }
    public bool Multiplayer { get; }
    public uint? LocalPlayerSlot { get; init; }
    public bool? HasActiveHero { get; init; }
    internal Func<string, bool?>? MatchActiveHero { get; init; }
    internal Action<WorldScriptExecutionContext, int>? EnterHeroPopup { get; init; }
    internal Func<uint, bool>? IsEnteredHeroReference { get; init; }
    public Sacred.Core.GameRes.GameResStore? ResourceText { get; init; }
    public IReadOnlyList<WorldScriptLocalGuiRequest> LocalGuiRequests { get; }
    internal void SubmitLocalGuiRequest(WorldScriptLocalGuiRequest request) => _localGuiRequests.Add(request);
    /// <summary>Native interpreter +0xA45C, initialized to zero at 0x461473.
    /// Supported startup commands do not set its deferred-action flag. A caller
    /// restoring another state must supply it; queue execution remains guarded.</summary>
    public uint ObjectActionFlags { get; init; }
    public Func<int>? Random { get; }
    public Func<int, int, bool>? PositionAllowed { get; }
    /// <summary>Gold function dispatch requires an active quest when FunkStru.Quest is nonnegative.
    /// Missing quest state is an unresolved execution dependency.</summary>
    public Func<int, bool?>? QuestActive { get; init; }
    /// <summary>Gold interpreter +0x18 while an eligible function runs. Null
    /// outside a proven dispatcher scope; restored across nested calls/failures.</summary>
    public int? CurrentFunctionQuest { get; internal set; }
    /// <summary>Associated creature identity in a compared invocation scope.
    /// Independent of the active-hero getter and interpreter +0x12.</summary>
    public uint AssociatedCreatureReference { get; internal set; }
    public WorldScriptQuests? Quests { get; init; }
    /// <summary>Creates/stores the native trigger and registers its name using world-trigger grants.
    /// An absent factory is required world work, not a successful declaration.</summary>
    public IWorldScriptTriggerRegistry? WorldTriggers { get; init; }
    public IWorldScriptObjectWorld? ObjectWorld { get; init; }
    public IWorldScriptCreatureWorld? CreatureWorld { get; init; }
    public WorldScriptRegions? ScriptRegions { get; init; }
    public WorldScriptMapIcons? MapIcons { get; init; }
    public WorldScriptMouseovers? Mouseovers { get; init; }
    public WorldScriptHideouts? Hideouts { get; init; }
    /// <summary>A consumer may reject an effect whose required runtime services are unavailable,
    /// before any mutation. Rejection is reported with the same file offset/call path as handler failure.</summary>
    public Action<SacredScriptCommand>? BeforeCommand { get; init; }
    /// <summary>Optional diagnostic observer of every command attempt, including predicates.
    /// Call paths and command bytes are copied so retained observations are independent.</summary>
    public Action<WorldScriptCommandTrace>? TraceCommand { get; init; }
    public WorldScriptSectorEnvironment? CurrentSector { get; internal set; }
    public bool IsInitializingSector { get; internal set; }
    public bool IsInitializingRegion { get; internal set; }
    /// <summary>Native interpreter +10 during EnterRegion, separate from Init.</summary>
    public bool IsEnteringRegion { get; internal set; }
    public bool IsEnteringSector { get; internal set; }
    /// <summary>Actual queue runtime flags during an owned sector callback.</summary>
    internal uint? CurrentSectorRuntimeFlags { get; set; }
    /// <summary>Actual local FIRSTCONTACT setting during the compared entry callback.</summary>
    public uint? LocalNetworkSpeedSettings { get; internal set; }
    public bool IsExecutingStartup { get; internal set; }
    public IReadOnlyList<SacredBaseTriggerDefinition> Triggers { get; }
    public IReadOnlyDictionary<ushort, int> Coverage { get; }
    /// <summary>Ordered opcode 115 rules, cleared at native EnterSector's callback boundary.</summary>
    public IReadOnlyList<SacredScriptSpawnEquipment> SpawnEquipment { get; }
    internal void ClearSpawnEquipment() => _spawnEquipment.Clear();
    internal void AddSpawnEquipment(SacredScriptSpawnEquipment rule) => _spawnEquipment.Add(rule);
    public int ExecutedCommands => _coverage.Values.Sum();
    public bool StartupCompleted { get; internal set; }
    /// <summary>Native loader continuation after bytecode/pool reload. Required
    /// callback effects and actor binding have their own completion state.</summary>
    public WorldScriptPostStartup PostStartup { get; }
    public bool IsInvalidated { get; internal set; }
    public WorldScriptExecutionFailure? Failure { get; internal set; }

    public static WorldScriptExecutionContext Create(WorldCampaignScripts scripts, bool multiplayer, Func<int>? random = null,
        Func<int, int, bool>? positionAllowed = null) =>
        new(WorldScriptDefinitions.Read(File.ReadAllBytes(scripts.Files.DefPosPath)), scripts.Functions, scripts.FunctionCode, multiplayer, random, positionAllowed)
        {
            Quests = new(scripts.Quests),
            EquipmentPools = scripts.EquipmentPoolArchive is { } pools ? new(pools) : new(),
            EquipmentPoolReload = scripts.EquipmentPoolArchive
        };

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
