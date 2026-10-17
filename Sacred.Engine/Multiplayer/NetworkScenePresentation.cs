using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Sacred.Core.Pak.Creature;
using Sacred.Engine.Assets;
using Sacred.Engine.Scene;
using Sacred.World.Networking;

namespace Sacred.Engine.Multiplayer;

/// <summary>Independent equipped, animated actors resolved from received references and original archives.</summary>
internal sealed class NetworkScenePresentation : IDisposable
{
    private readonly AssetManager _assets;
    private readonly SceneState _scene;
    private readonly NetworkCharacterUiBinding _characterUi;
    private readonly Dictionary<uint, (string Key, Task<NetworkActorPresentation> Load)> _pending = [];
    private readonly Dictionary<uint, (string Key, NetworkActorPresentation Actor)> _actors = [];
    private readonly Dictionary<uint, string> _failed = [];
    private readonly CancellationTokenSource _stop = new();
    private SceneModel[] _published = [];
    private bool _localReported;
    private bool _disposed;
    public NetworkScenePresentation(SacredNetworkWorldBinding world, AssetManager assets, SceneState scene)
    {
        if (world.LocalHero.BaseState.Surface != 0) throw new NotSupportedException("Network indoor actor binding is pending; surface metadata is retained.");
        (World, _assets, _scene) = (world, assets, scene);
        _characterUi = new(assets, scene);
        if (assets.NpcConversations is { } definitions) Conversations = new(world.Replicas, definitions);
        scene.NpcOverheads.DialogIcon = world.Replicas.ScriptDialogs.GetIcon;
    }
    public SacredNetworkWorldBinding World { get; }
    public SacredNetworkNpcConversations? Conversations { get; }
    public int ModelCount => _actors.Count;
    public int AnimatedCount => _actors.Values.Count(entry => entry.Actor.Animated);
    public bool LocalModelReady => _actors.ContainsKey(World.PlayerSlot);
    public void Update() => Update(1.0f / 60);

    public void Update(float deltaSeconds)
    {
        if (_disposed || !World.IsCurrentSession) return;
        World.Update();
        Conversations?.Update(deltaSeconds);
        _characterUi.Update(World.Replicas);
        var creatures = World.WorldObjects.Where(obj => obj.Creature is not null && obj.BaseState.Surface == 0)
            .OrderBy(obj => obj.BaseState.ObjectReference == World.PlayerSlot ? 0 : 1).ToArray();
        var visible = creatures.Select(obj => obj.BaseState.ObjectReference).ToHashSet();
        var overheads = new List<NpcOverheadActor>();
        foreach (var reference in _actors.Keys.Where(id => !visible.Contains(id)).ToArray()) _actors.Remove(reference);
        foreach (var pair in _pending.Where(entry => entry.Value.Load.IsCompleted).ToArray())
        {
            _pending.Remove(pair.Key);
            try
            {
                var actor = pair.Value.Load.GetAwaiter().GetResult();
                if (!visible.Contains(pair.Key) || !World.Replicas.Objects.TryGetValue(pair.Key, out var current) ||
                    NetworkActorAppearance.Key(current, World.Replicas) != pair.Value.Key) continue;
                _actors[pair.Key] = (pair.Value.Key, actor);
                if (pair.Key == World.PlayerSlot) _scene.SetModel(0, actor.Model);
                EngineLog.WriteLine($"Network actor ready: ref {pair.Key}; animated {actor.Animated}; native equipped model composed.");
            }
            catch (Exception error)
            {
                _failed[pair.Key] = pair.Value.Key;
                EngineLog.WriteLine($"Network actor {pair.Key} could not load: {error.Message}.");
            }
        }
        foreach (var creature in creatures)
        {
            var reference = creature.BaseState.ObjectReference;
            var key = NetworkActorAppearance.Key(creature, World.Replicas);
            if ((!_actors.TryGetValue(reference, out var entry) || entry.Key != key) && _pending.Count < 4 &&
                !_pending.ContainsKey(reference) && _failed.GetValueOrDefault(reference) != key && _assets.GetItem(creature.BaseState.ObjectType) is { } item)
            {
                var loadout = NetworkActorAppearance.Create(_assets, creature, World.Replicas);
                var position = SacredNetworkPosition.ToTiles(creature.BaseState);
                var template = _assets.CreatureTemplates?.ResolveTemplate(item);
                var cancellation = _stop.Token;
                _pending[reference] = (key, Task.Run(async () =>
                {
                    var asset = await _assets.LoadNetworkActorAsync(item, loadout, cancellation).ConfigureAwait(false);
                    var animations = await _assets.LoadNetworkAnimationsAsync(asset, template, cancellation).ConfigureAwait(false);
                    return new NetworkActorPresentation(reference, asset, animations, item, position)
                        { IsLocal = reference == World.PlayerSlot, TerrainHeight = World.OutdoorHeightAt };
                }, cancellation));
            }
            entry.Actor?.Update(creature, World.OutdoorHeight(creature.BaseState), deltaSeconds);
            var conversationFacing = false;
            if (entry.Actor is not null && Conversations is { } conversations)
            {
                if (conversations.States.GetValueOrDefault(reference) is { NeedsAnimation: true } waiting)
                    entry.Actor.ConversationTiming = NetworkNpcAnimationSpeed.Resolve(_assets, World.Replicas, creature, entry.Actor.ConversationMotionFactor(waiting));
                conversationFacing = entry.Actor.ApplyConversation(conversations.States.GetValueOrDefault(reference), conversations);
            }
            if (entry.Actor is not null && _assets.GetItem(creature.BaseState.ObjectType) is { } overheadItem)
            {
                var template = _assets.CreatureTemplates?.ResolveTemplate(overheadItem);
                var overhead = Conversations?.OverheadState(creature, World.LocalHero) ?? SacredNetworkNpcPresentation.OverheadState(creature, World.LocalHero);
                overheads.Add(new(reference, entry.Actor.Model, overhead, overheadItem.ModelDesc.HasNpcDialogMarker,
                    template?.Class == SacredCreatureClass.Animal));
                if (!conversationFacing && creature.Movement is null && creature.Values is { CurrentHitPoints: > 0 } &&
                    SacredNpcClientRules.FacesLocalHero(reference, overhead, template?.Class == SacredCreatureClass.Horse))
                    entry.Actor.FaceLocalHero(World.LocalPosition);
            }
        }
        _scene.NpcOverheads.Actors = overheads.ToArray();
        var next = _actors.Where(entry => entry.Key != World.PlayerSlot).OrderBy(entry => entry.Key).Select(entry => entry.Value.Actor.Model).ToArray();
        if (!_published.SequenceEqual(next)) { _published = next; _scene.SetReplicatedModels(next); }
        if (!_localReported && LocalModelReady)
        {
            _localReported = true;
            EngineLog.WriteLine($"Network local model bound: player {World.PlayerSlot}, resource {World.LocalHero.BaseState.ObjectType}, tile {World.LocalPosition.X:0.###}/{World.LocalPosition.Y:0.###}; equipped actor animation ready.");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Conversations?.Clear();
        World.Dispose();
        _stop.Cancel();
        var pending = _pending.Values.Select(entry => entry.Load).ToArray();
        foreach (var entry in _pending.Values)
            _ = entry.Load.ContinueWith(task => { _ = task.Exception; }, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        _pending.Clear(); _actors.Clear(); _scene.SetReplicatedModels([]);
        _scene.NpcOverheads.Actors = [];
        _scene.NpcOverheads.DialogIcon = _ => null;
        _ = Task.WhenAll(pending).ContinueWith(task => { _ = task.Exception; _stop.Dispose(); }, CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }
}
