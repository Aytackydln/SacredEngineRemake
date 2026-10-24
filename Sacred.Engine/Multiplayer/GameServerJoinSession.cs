using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Sacred.Core.GameBin.Scripts;
using Sacred.Core.Pak.Creature;
using Sacred.Core.Pak.Items;
using Sacred.Core.Pax.Hero;
using Sacred.Core.World;
using Sacred.GameplayHost;
using Sacred.GameplayHost.Networking;
using Sacred.Network.Admission;
using Sacred.Network.Gameplay;
using Sacred.World.Networking;

namespace Sacred.Engine.Multiplayer;

/// <summary>Runs saved participant admission through a rendered world without timed command sequences.</summary>
internal sealed class GameServerJoinSession
{
    private readonly GameServerClientController _client;
    private readonly string _endpoint;
    private readonly int _selection;
    private readonly Task<(GoldFirstContact Contact, SacredSavedCharacterRecords Source, SacredSectorIndex Sectors, IReadOnlyDictionary<uint, SacredCreatureClass> Classes, IReadOnlyDictionary<uint, uint> Resources)> _load;
    private (GoldFirstContact Contact, SacredSavedCharacterRecords Source, SacredSectorIndex Sectors, IReadOnlyDictionary<uint, SacredCreatureClass> Classes, IReadOnlyDictionary<uint, uint> Resources) _data;
    private Task? _ready;
    private Task? _banksRequest;
    private readonly Task<IReadOnlyDictionary<uint, SacredItemCategory>> _categories;
    private readonly string _folder;
    private Task<WorldCampaignScripts>? _scriptLoad;
    private Task<WorldCreatureConstructionAssets>? _constructionLoad;
    private WorldParticipantSectorExecutor? _sectorWork;
    private SacredNetworkWorldBinding? _patchWorld;
    public void SetSectorPatchWorld(SacredNetworkWorldBinding world)
    {
        _patchWorld = world;
        if (_sectorWork is not null) _sectorWork.ApplySectorPatches = world.TryApplySectorPatches;
    }
    public string? SectorScriptSelection { get; init; }
    public string SectorWorkStatus => _sectorWork?.Status ?? "sector executor not loaded";
    private int _phase;
    private DateTime _lastAdvance = DateTime.UtcNow;
    public bool IsFinished => _phase < 0 || _phase == 6;
    public string Status { get; private set; } = "loading original saved character and LAN compatibility";
    public GoldConnectionMode ConnectionMode { get; }

    public GameServerJoinSession(GameServerClientController client, string folder, string endpoint, int selection)
        : this(client, folder, endpoint, selection, GoldConnectionMode.LanHighBandwidth) { }

    public GameServerJoinSession(GameServerClientController client, string folder, string endpoint, int selection, GoldConnectionMode connectionMode)
    {
        _ = GoldConnectionModes.Label(connectionMode);
        ConnectionMode = connectionMode;
        if (client.IsConnected) throw new InvalidOperationException("Disconnect before joining another character.");
        if (selection is < 1 or > 8) throw new ArgumentOutOfRangeException(nameof(selection));
        (_client, _endpoint, _selection) = (client, endpoint, selection);
        _folder = folder;
        _categories = Task.Run<IReadOnlyDictionary<uint, SacredItemCategory>>(() => GameServerCreatureClasses.ReadObjectCategories(folder));
        _load = Task.Run(() =>
        {
            var compatibility = GoldLanCompatibility.Read(folder);
            var hero = SacredHeroPaxProfile.ReadSelected(folder, selection);
            var source = GameServerSavedCharacterReader.Read(folder, selection);
            var contact = GoldFirstContact.Create(hero.Profile, new(hero.Name, (byte)selection, 1, (byte)connectionMode, 1, 0), compatibility);
            return (contact, source, SacredSectorIndex.Read(Path.Combine(folder,"World","sectors.keyx")),
                (IReadOnlyDictionary<uint, SacredCreatureClass>)GameServerCreatureClasses.Read(folder),
                (IReadOnlyDictionary<uint, uint>)GameServerCreatureClasses.ReadObjectResources(folder));
        });
        EngineLog.WriteLine($"Network join: {Status}; {GoldConnectionModes.Label(ConnectionMode)} (native setting {(byte)ConnectionMode}).");
    }

    public void Update(Func<GoldWorldReplica,bool> bindScene, Func<bool> sceneReady)
    {
        if (_phase < 0) return;
        try
        {
            if (_scriptLoad is { IsCompleted: true } scripts && _sectorWork is null && _client.WorldReplicas.LocalPlayerSlot != 0)
            {
                _sectorWork = new(_client.WorldReplicas, _data.Sectors, scripts.GetAwaiter().GetResult(), EngineLog.WriteLine)
                    { NetworkSpeedSettings = _data.Contact.ConnectionSpeed };
                if (_patchWorld is not null) _sectorWork.ApplySectorPatches = _patchWorld.TryApplySectorPatches;
            }
            if(_sectorWork is not null && _patchWorld is not null && _constructionLoad is {IsCompleted:true} construction)
                _sectorWork.ConfigurePopulation(construction.GetAwaiter().GetResult(),_client.ObjectReferences,_patchWorld,_client);
            _sectorWork?.Update();
            if (_phase == 6) return;
            if (_client.WorldReplicas.SectorWork.FailedCount != 0)
                throw new InvalidOperationException(_client.WorldReplicas.SectorWork.Work.Values.First(work => work.State == GoldSectorWorkState.Failed).Error);
            if (_sectorWork?.PendingRegion is not null || _sectorWork?.PendingPopulation is not null)
            {
                _lastAdvance = DateTime.UtcNow;
                Status = "waiting for native sector work: " + _sectorWork.Status;
            }
            if (DateTime.UtcNow - _lastAdvance > TimeSpan.FromSeconds(90))
                throw new TimeoutException("No join progress for 90 seconds. Disconnect and check the server/world diagnostics before retrying.");
            switch (_phase)
            {
                case 0 when _load.IsCompleted && _categories.IsCompleted:
                    _data = _load.GetAwaiter().GetResult();
                    _client.SetFirstContact(_data.Contact);
                    _client.TrySetOption("server",_endpoint,out var connected);
                    _client.WorldReplicas.ConfigureCreatureClasses(_data.Classes);
                    _client.WorldReplicas.ConfigureObjectResources(_data.Resources);
                    _client.WorldReplicas.ConfigureObjectCategories(_categories.GetAwaiter().GetResult());
                    _client.WorldReplicas.MovementTriggers.ConfigureSectors(_data.Sectors);
                    _client.WorldReplicas.SpecialFx.ConfigureSectors(_data.Sectors);
                    Advance(1, connected + $"; saved selection {_selection}, {_data.Classes.Count} creature class definitions loaded");
                    break;
                case 1 when _client.PlayerSlot != 0 && _client.GrantedReferenceCount >= _data.Source.Objects.Records.ToArray().Count(record=>!record.IsEmpty):
                    _client.StageCharacterReferences(_selection,_data.Source);
                    _client.StartCharacterUpload();
                    Advance(2,"waiting for native participant approval");
                    break;
                case 2 when _client.IsParticipantWorldApproved && _client.WorldReplicas.LocalPlayerSlot != 0:
                    _scriptLoad = Task.Run(() => WorldCampaignScripts.Load(SacredCampaignFiles.Resolve(_folder, SectorScriptSelection)));
                    _constructionLoad = Task.Run(() => WorldCreatureConstructionAssets.Load(_folder));
                    _banksRequest = _client.RequestWorldBanksAsync();
                    _client.RequestInitialSectors(_data.Sectors);
                    Advance(3,"waiting for native script/world banks and confirmed terrain");
                    break;
                case 3 when _client.WorldReplicas.Sectors.Count > 0:
                    if (!_banksRequest!.IsCompleted) break;
                    _banksRequest.GetAwaiter().GetResult();
                    if (!_client.WorldReplicas.WorldBanks.IsComplete || !bindScene(_client.WorldReplicas)) break;
                    Advance(4,"native script/world banks received; loading server world into the scene");
                    break;
                case 4 when sceneReady():
                    if (_sectorWork is null || !_client.WorldReplicas.SectorWork.IsComplete) break;
                    _ready = _client.SendPeerReadyAsync();
                    Advance(5,"sending native peer readiness after local model binding");
                    break;
                case 5 when _ready!.IsCompleted:
                    _ready.GetAwaiter().GetResult();
                    Advance(6,$"in game: Sacred slot {_client.PlayerSlot}, saved selection {_selection}; connection stays open");
                    break;
            }
            if (_phase is >= 1 and <= 5 && !_client.IsConnected)
                throw new InvalidOperationException("Connection ended during join: " + _client.Status);
        }
        catch (Exception error)
        {
            _phase = -1;
            Status = "failed: " + error.Message;
            _client.TrySetOption("server", "disconnect", out _);
            EngineLog.WriteLine($"Network join {Status}.");
        }
    }

    public void Cancel()
    {
        _phase = -1;
        Status = "cancelled / disconnected";
        // Loading reads only native files; observe any failure even when cancellation precedes completion.
        _ = _load.ContinueWith(task => { _ = task.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
        _ = _categories.ContinueWith(task => { _ = task.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
        if (_ready is { } ready)
            _ = ready.ContinueWith(task => { _ = task.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
        if (_scriptLoad is { } scripts)
            _ = scripts.ContinueWith(task => { _ = task.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
        if (_constructionLoad is { } construction)
            _ = construction.ContinueWith(task => { _ = task.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
        if (_banksRequest is { } banks)
            _ = banks.ContinueWith(task => { _ = task.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
        EngineLog.WriteLine($"Network join: {Status}.");
    }

    private void Advance(int phase,string status) { _phase=phase; _lastAdvance=DateTime.UtcNow; Status=status; EngineLog.WriteLine($"Network join: {Status}."); }
}
