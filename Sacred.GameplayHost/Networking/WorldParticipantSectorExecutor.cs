using System.Collections.ObjectModel;
using Sacred.Assets.Executable;
using Sacred.Assets.GameBin;
using Sacred.Core.GameBin.Scripts;
using Sacred.Core.World;
using Sacred.GameplayHost.Scripting;
using Sacred.Network.Gameplay;

namespace Sacred.GameplayHost.Networking;

/// <summary>Executes ordered participant initialization and delegates the zero-mode patch
/// pass to bound terrain. Missing region services keep the operation pending; unsupported entry and factory effects fail it;
/// later work cannot overtake terrain waits or failures.</summary>
public sealed class WorldParticipantSectorExecutor
{
    private readonly GoldWorldReplica _replicas;
    private readonly SacredSectorIndex _index;
    private readonly SacredSectorGrid _grid;
    private readonly WorldScriptExecutionContext _context;
    private readonly WorldScriptSectorCallbacks _callbacks;
    private readonly Action<string> _log;
    private readonly long _session;
    private readonly Dictionary<ushort, WorldScriptSectorEnvironment> _environments = [];
    private GoldWorldBankSnapshot? _definitionsBank;
    private GoldSectorWork? _executing;
    private bool _initialized;
    private WorldParticipantCreatureRequest? _pendingCreature;
    private readonly SacredRegionDefinitions? _regions;
    public Func<GoldSectorWork, bool>? ApplySectorPatches { get; set; }
    public WorldParticipantSectorExecutor(GoldWorldReplica replicas, SacredSectorIndex index,
        WorldCampaignScripts scripts, Action<string> log)
    {
        ArgumentNullException.ThrowIfNull(replicas);
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(scripts);
        ArgumentNullException.ThrowIfNull(log);
        if (replicas.LocalPlayerSlot == 0) throw new InvalidOperationException("Sector execution requires a seeded participant session.");
        (_replicas, _index, _log, _session) = (replicas, index, log, replicas.SessionVersion);
        _regions = scripts.Regions;
        _grid = index.CreateGrid();
        // Definitions and variables are imported from the current native host state, rather
        // than recreating startup or overwriting it with the original DefPos defaults.
        _context = new(new(), scripts.Functions, scripts.FunctionCode, true) { BeforeCommand = RequireSupportedEffect };
        _callbacks = new(_context, new(_context));
        Environments = new ReadOnlyDictionary<ushort, WorldScriptSectorEnvironment>(_environments);
    }
    public IReadOnlyDictionary<ushort, WorldScriptSectorEnvironment> Environments { get; }
    public int InitializedSectors { get; private set; }
    public WorldParticipantCreatureRequest? PendingCreature => IsCurrentSession ? _pendingCreature : null;
    private WorldParticipantRegionRequirement? _pendingRegion;
    public WorldParticipantRegionRequirement? PendingRegion => IsCurrentSession ? _pendingRegion : null;
    public string Status { get; private set; } = "waiting for native sector work";
    public bool IsCurrentSession => _session == _replicas.SessionVersion && _replicas.LocalPlayerSlot != 0;

    public void Update(CancellationToken cancellationToken = default)
    {
        if (!IsCurrentSession)
        {
            _environments.Clear();
            _executing = null;
            _pendingCreature = null;
            _pendingRegion = null;
            Status = "session ended";
            return;
        }
        var queue = _replicas.SectorWork;
        if (queue.PendingCount == 0 || queue.FailedCount != 0) return;
        if (!_replicas.WorldBanks.Current.TryGetValue(GoldWorldBankLayout.Bank1Opcode, out _) ||
            !_replicas.WorldBanks.Current.TryGetValue(GoldWorldBankLayout.Bank2Opcode, out var definitions) ||
            _replicas.Regions.Table is null)
        {
            Status = "waiting for native script-variable, definition and region banks";
            return;
        }
        var work = _executing ?? queue.BeginNext();
        if (work is null) return;
        _executing = work;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (work.ExecuteScripts || work.Entry.RequestsInitialization && !_initialized)
            {
                if (_replicas.ScriptVariables.UnknownSlotCount != 0)
                    throw new InvalidDataException("Native script-variable state has unknown slots; callback predicates cannot be resolved.");
                if (!ReferenceEquals(definitions, _definitionsBank))
                {
                    _context.Definitions.Import(definitions.Sections[5].Records.Select(row => WorldScriptDefinition.FromPayload(row.Data)));
                    _definitionsBank = definitions;
                }
                _context.Variables.Import(_replicas.ScriptVariables.Records.OrderBy(pair => pair.Key).Select(pair => pair.Value));
            }
            if (!_index.TryGetSector(work.Entry.Sector, out var source))
                throw new InvalidDataException($"Sector {work.Entry.Sector} is absent from original KEYX data.");
            if (!_environments.TryGetValue(work.Entry.Sector, out var sector))
                _environments.Add(work.Entry.Sector, sector = new(source));
            if (work.Entry.RequestsInitialization && !_initialized)
            {
                // Gold initRegion returns without invoking its callback when the received
                // region singleton already has bit 1. Uninitialized regions need the full
                // native raw-region -> script-region mapping and mutable region services.
                if (!_replicas.Regions.Table.IsInitialized(source.Environment.Region))
                {
                    var requirement = new WorldParticipantRegionRequirement(work.Entry.Sector, source.Environment.Region,
                        _regions?.ScriptRegion(source.Environment.Region));
                    if (PendingRegion != requirement) _log("Network sector work pending: " + requirement + ".");
                    _pendingRegion = requirement;
                    Status = requirement.ToString();
                    return;
                }
                _pendingRegion = null;
                var invoked = _callbacks.Initialize(sector, cancellationToken);
                InitializedSectors++;
                _initialized = true;
                _log($"Network sector initialization: {sector.Sector}, callback {(invoked ? "executed" : "absent")}; monster maximum {sector.Value.MonsterMaxCount}, levels {sector.Value.MonsterMinLevel}..{sector.Value.MonsterMaxLevel}.");
            }
            if (work.ExecuteScripts)
                throw new InvalidDataException($"Sector {source.Id} requires native enter-region handling, enter callback, object generation, tile patches and enter notification; initialization alone cannot complete entry.");
            if (work.ApplyPatches && (ApplySectorPatches is null || !ApplySectorPatches(work)))
            {
                Status = $"sector {source.Id} waiting for confirmed terrain and patch vectors";
                return;
            }
            queue.Complete(work);
            _executing = null;
            _initialized = false;
            Status = $"sector {source.Id} work completed";
            _log("Network sector work: " + Status + ".");
        }
        catch (Exception error)
        {
            queue.Fail(work, error.Message);
            Status = $"sector {work.Entry.Sector} failed: {error.Message}";
            _log("Network sector work failed: " + Status);
        }
    }

    private void RequireSupportedEffect(SacredScriptCommand command)
    {
        if (command.Opcode == SacredScriptCreateCreature.Opcode)
        {
            var creation = SacredScriptCreatureCreationReader.Read(command,
                name => _context.Definitions.ResolvePosition(name, _context.Random, _context.PositionAllowed), _context.Variables.Find);
            var placement = WorldScriptCreaturePlacement.Resolve(creation, _grid);
            SacredScriptCreatureReplacement? replacement = null;
            // Gold 0x482F18: the supported subset has no special command-mode flags;
            // 0x6B must explicitly select zero. Omitted input -1 skips substitution.
            if (creation.Flag2B7Mask08Input == 0 && placement is { } position && _index.TryGetSector(position.Sector, out var placedSector))
                replacement = SacredScriptRegionReplacementTable.Read(
                    _replicas.WorldBanks.Current[GoldWorldBankLayout.Bank1Opcode].Globals!.SecondBlock)
                    .Resolve(placedSector.Environment.Region, creation.TypeId, creation.Behavior);
            var named = creation.Name is null ? GoldScriptResourceLayout.UnnamedResource :
                _replicas.ScriptResources.FindLocalResource(creation.Name);
            var existing = named is { } resource && resource is not 0 and not uint.MaxValue ? _replicas.FindScriptObject(resource) : null;
            _pendingCreature = new(command.FileOffset, creation, placement)
                { NamedResource = named, ExistingObjectReference = existing, RegionalReplacement = replacement };
            var target = placement is { } point ? $"{point.X},{point.Y},{point.Surface}, sector {point.Sector}" : "native early-return placement";
            // This preflight deliberately earns no execution credit. Even an early
            // return can consume RNG/name state before placement, and constructing an
            // actor also requires native level, equipment and publication services.
            throw new InvalidDataException($"CreateNPC resource {_pendingCreature.EffectiveTypeId} (authored {creation.TypeId}), position {target}, behavior 0x{_pendingCreature.EffectiveBehaviorFlags:X8}, aggression {creation.Aggression}, group {creation.Group}, level input {creation.LevelInput} requires native RNG/name handling, creature construction, level/value initialization, equipment and publication services.");
        }
        if (command.Opcode is SacredScriptControlOpcodes.Nop or SacredScriptExecutionOpcodes.CallFunction or
            SacredScriptExecutionOpcodes.DefinePosition or SacredScriptExecutionOpcodes.DefinePoolPosition or
            SacredScriptExecutionOpcodes.SetSectorMonsterParameters) return;
        throw new InvalidDataException($"Participant sector opcode {command.Opcode} requires its native world effect/publication service.");
    }
}
