using Sacred.Assets.Executable;
using Sacred.Assets.GameBin;
using Sacred.Core.World;
using Sacred.GameplayHost.Scripting;
using Sacred.Network.Gameplay;
using Sacred.Network.Protocol;

namespace Sacred.GameplayHost.Networking;

/// <summary>Private host region initialization on already loaded sectors. The
/// constructor baseline is established; visited flags are not final world globals.
/// Effectful callbacks remain pending until their caller services bind.</summary>
public sealed class WorldHostRegionInitialization
{
    private readonly WorldHostStartupState _startup;
    private readonly SacredSectorIndex _index;
    private readonly SacredRegionDefinitions _mapping;
    private readonly WorldScriptRegionFunctionBindings _bindings;
    private readonly byte _owner;
    private readonly List<GoldMessage> _publications = [];
    private uint? _pendingSector;
    public WorldScriptRegionInitialization Lifecycle { get; } = new();
    public GoldWorldRegionTable Regions { get; private set; } = GoldWorldRegionTable.CreateInitial();
    public IReadOnlyList<GoldMessage> Publications => _publications.AsReadOnly();
    public string? Blocker { get; private set; }

    internal WorldHostRegionInitialization(string installation, WorldHostStartupState startup, byte owner)
    {
        (_startup, _owner) = (startup, owner);
        _index = SacredSectorIndex.Read(Path.Combine(installation, "World", "sectors.keyx"));
        _mapping = SacredRegionDefinitions.Load(installation);
        _bindings = new(startup.Execution.Functions.Select(function => function.Name).ToArray(), SacredRegionFunctionBindingDefinitions.Load(installation));
    }

    public bool? TryInitialize(uint sector, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (_startup.Execution.IsInvalidated || !_startup.Execution.StartupCompleted)
            throw new InvalidOperationException("Region initialization needs completed valid startup.");
        if (_pendingSector is { } pending && pending != sector)
            throw new InvalidOperationException($"Region callback for sector {pending} is still pending.");
        var services = new WorldScriptRegionInitializationServices(ReadRegion, _mapping.ScriptRegion,
            raw => Regions.GetFlags(raw), (raw, mask) =>
            {
                Regions = Regions.WithFlags(raw, mask, true);
                _publications.Add(GoldWorldRegionFlagsCodec.Create(_owner, raw, mask, true));
            }, _bindings.Find, FindFunction, index => Invoke(index, token));
        Blocker = null;
        var result = Lifecycle.HasPendingCallback ? Lifecycle.Continue(services) : Lifecycle.TryInitialize(sector, true, true, services);
        _pendingSector = result is null ? sector : null;
        return result;
    }
    private byte ReadRegion(uint sector)
    {
        if (!_index.TryGetSector(sector, out var source)) throw new InvalidDataException("Region sector is absent from original KEYX.");
        if (_startup.Objects.Creatures?.Terrain?.Tiles.IsLoaded(sector) != true)
            throw new NotSupportedException("Region initialization first requires the reached sector demand-load/patch effects.");
        return source.Environment.Region;
    }
    private int FindFunction(string name)
    {
        for (var index = 0; index < _startup.Execution.Functions.Count; index++)
            if (WorldScriptNameComparer.Instance.Equals(_startup.Execution.Functions[index].Name, name)) return index;
        return -1;
    }
    private bool Invoke(ushort index, CancellationToken token)
    {
        var completed = WorldScriptRegionCallbackProfile.TryInvoke(_startup.Execution, index, token, out var blocker);
        Blocker = blocker;
        return completed;
    }
}
