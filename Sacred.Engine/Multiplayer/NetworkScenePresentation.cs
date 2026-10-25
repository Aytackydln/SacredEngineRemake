using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Sacred.Core.Pak.Creature;
using Sacred.Engine.Animation;
using Sacred.Engine.Assets;
using Sacred.Engine.Scene;
using Sacred.Engine.Scene.InGame;
using Sacred.GameplayHost.World;
using Sacred.Network.Gameplay;
using Sacred.Particles;
using Sacred.World.Networking;
using Sacred.World.Particles;

namespace Sacred.Engine.Multiplayer;

/// <summary>Independent equipped, animated actors resolved from received references and original archives.</summary>
internal sealed class NetworkScenePresentation : IDisposable
{
    private readonly AssetManager _assets;
    private readonly SceneState _scene;
    private readonly NetworkCharacterUiBinding _characterUi;
    private readonly NetworkCombatPresentation _combat = new();
    private readonly NetworkCreatureAnimationConsumer _creatureAnimations = new();
    private readonly NetworkHitReactionConsumer _hitReactions = new();
    private readonly NetworkCreatureDeathConsumer _deaths = new();
    private readonly NetworkWorldTextPresentation _worldText = new();
    private readonly NetworkHudPresentation _hudNotices = new();
    private readonly SacredNetworkCreatureMarkers _markers = new();
    private readonly WorldMeleeDeflectionPrediction _meleeDeflections = new();
    public void UpdateMeleeDeflections(float seconds, WorldCombatDeflectionSystem effects) =>
        _meleeDeflections.Update(World.Replicas, World.Replicas.Session.SimulationSeconds(seconds),
            subject => World.CanNavigateSector(subject.BaseState.Sector) ? FxActor(subject) : null,
            attack => _actors.TryGetValue(attack.ObjectReference, out var entry) ? entry.Actor.MeleeImpactTime(attack) : null,
            effects.EmitPredicted);
    public IReadOnlyList<WorldCreatureMarker> GroundMarkers => _markers.Markers;
    public WorldReceivedFxSystem? DeathEffects { get; init; }
    public SacredParticleQuality ParticleQuality { get; set; } = SacredParticleQuality.High;
    private uint? _hoveredCreature;
    internal bool DebugHealthHoverEnabled { get; set; }
    internal uint? DebugHealthHover { get; set; }
    public void UpdateCreatureHover(SacredCamera camera, Vector2 pointer, int width, int height, bool enabled)
    {
        _hoveredCreature = DebugHealthHoverEnabled ? DebugHealthHover : null;
        if (DebugHealthHoverEnabled || !enabled) return;
        var best = float.PositiveInfinity;
        foreach (var (reference, entry) in _actors)
              if (reference != World.PlayerSlot && World.Replicas.Objects.TryGetValue(reference, out var current) &&
                  current.InstanceVersion == entry.Actor.InstanceVersion && !current.IsDead && !_deaths.IsHidden(current) &&
                  (current.BaseState.Flags & GoldObjectLifecycleLayout.InactiveFlag) == 0 &&
                WorldModelFocusPicker.TryScore(entry.Actor.Model, camera, pointer, width, height, out var score) && score < best)
            { _hoveredCreature = reference; best = score; }
    }
    private readonly NetworkBowProjectiles _projectiles;
    private readonly CharacterAnimationBatch _animationBatch = new();
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
        _projectiles = new(assets);
        _characterUi = new(assets, scene);
        if (assets.NpcConversations is { } definitions) Conversations = new(world.Replicas, definitions);
        scene.NpcOverheads.DialogIcon = world.Replicas.ScriptDialogs.GetIcon;
    }
    public SacredNetworkWorldBinding World { get; }
    public required IGameplayObjectWorld GameplayObjects { get; init; }
    public SacredNetworkNpcConversations? Conversations { get; }
    public int ModelCount => _actors.Count;
    public int AnimatedCount => _actors.Values.Count(entry => entry.Actor.Animated);
    public bool LocalModelReady => _actors.ContainsKey(World.PlayerSlot);
    public float LocalLocomotionScale { get; set; } = 1;
    public WorldObjectFxAttachment? FxActor(GoldReplicatedObject snapshot)
    {
        var reference = snapshot.BaseState.ObjectReference;
        if (!_actors.TryGetValue(reference, out var entry) || entry.Actor.InstanceVersion != snapshot.InstanceVersion ||
            snapshot.IsDead && !entry.Actor.DeathPoseReady ||
            _assets.GetItem(snapshot.BaseState.ObjectType) is not { } item) return null;
        var model = entry.Actor.Model;
        var point = GameplayProjectileCoordinates.FromScene(model.RenderPosition);
        return new(GameplayProjectileCoordinates.ToTiles(point), point.Z, item.ModelDesc.BlockRadius,
            new ModelParticleSeedVertices(model, SacredParticleCatalogue.LoadEmbedded().Projection))
        { InstanceVersion = snapshot.InstanceVersion, BoneAttachment = bone =>
            SacredFxAttachments.BoneName(bone) is { } name && entry.Actor.TryGetFxAttachment(name, out var socket)
                ? new(GameplayProjectileCoordinates.ToTiles(socket), socket.Z, item.ModelDesc.BlockRadius)
                    { InstanceVersion = snapshot.InstanceVersion } : null };
    }
    public WorldObjectFxAttachment? ParticleAttachment(uint reference, ushort bone)
    {
        if (!World.Replicas.Objects.TryGetValue(reference, out var actor) ||
            (actor.BaseState.Flags & GoldObjectLifecycleLayout.InactiveFlag) != 0) return null;
        var item = _assets.GetItem(actor.BaseState.ObjectType);
        if (item is null) return null;
        // Sentinel requests lifetime only, allowing assets/poses to load without losing FX.
        if (bone == ushort.MaxValue)
            return new(new(), 0, item.Value.ModelDesc.BlockRadius) { InstanceVersion = actor.InstanceVersion };
        var name = SacredFxAttachments.BoneName(bone);
        if (name is null || !TryGetFxAttachment(reference, name, out var point)) return null;
        return new(GameplayProjectileCoordinates.ToTiles(point), point.Z, item.Value.ModelDesc.BlockRadius)
        { InstanceVersion = actor.InstanceVersion };
    }
    public bool TryGetFxAttachment(uint reference, string bone, out Vector3 position)
    {
        position = default;
        return _actors.TryGetValue(reference, out var entry) && entry.Actor.TryGetFxAttachment(bone, out position);
    }
    public void Update() => Update(1.0f / 60);

    public void Update(float deltaSeconds)
    {
        if (_disposed || !World.IsCurrentSession) return;
        deltaSeconds = World.Replicas.Session.SimulationSeconds(deltaSeconds);
        _creatureAnimations.Update(World.Replicas, _actors, deltaSeconds);
        _hitReactions.Update(World.Replicas, _actors, type => _assets.GetItem(type) is { } item
            ? _assets.CreatureTemplates?.ResolveTemplate(item)?.Class ?? 0 : 0, deltaSeconds);
        GameplayObjects.Update(World.VisibleWorld, null);
        if (GameplayObjects.CombatArtEffects is { } effects)
            effects.CastSpeedResolver ??= (actor, code) => NetworkCastSpeed.ResolveForArt(_assets, World.Replicas, actor, code);
        GameplayObjects.Advance(deltaSeconds);
        Conversations?.Update(deltaSeconds);
        _characterUi.Update(World.Replicas);
        var creatures = GameplayObjects.Objects.Values.Select(obj => obj.Replica).OfType<GoldReplicatedObject>()
            .Concat(DeathEffects?.AwaitingActors.Where(snapshot => World.Replicas.Objects.TryGetValue(
                snapshot.BaseState.ObjectReference, out var current) && current.InstanceVersion == snapshot.InstanceVersion)
                .Select(snapshot => snapshot with { BaseState = snapshot.BaseState with
                    { Flags = snapshot.BaseState.Flags | GoldObjectLifecycleLayout.InactiveFlag } }) ?? [])
            .DistinctBy(obj => obj.BaseState.ObjectReference)
            .Where(obj => obj.Creature is not null && obj.BaseState.Surface == 0)
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
                if (_actors.TryGetValue(pair.Key, out var previous)) actor.ContinueCombatFrom(previous.Actor);
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
        _deaths.Update(creatures, _actors, deltaSeconds);
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
                        { IsLocal = reference == World.PlayerSlot, TerrainHeight = World.OutdoorHeightAt,
                            LocomotionSpeedResolver = (current, action, pathSpeed) => reference == World.PlayerSlot && _scene.Portraits.Player is { } player
                                ? player.LocomotionPlaybackSpeed * LocalLocomotionScale
                                : NetworkLocomotionSpeed.Resolve(_assets.MovementCalculator, current.Values?.MovementSpeedPercent,
                                    current.Creature!.AnimationSpeedMultiplier, template, current.Creature.BehaviorFlags, action, pathSpeed),
                            GlobalAnimationScale = _assets.MovementCalculator?.GlobalAnimationScale ?? 1,
                            CombatArtSelector = current => NetworkCombatArtSelection.Resolve(_assets, World.Replicas, current),
                            CombatArtTiming = (current, duration) => GameplayObjects.StatusEffects?.SetActionTiming(current, duration),
                            CombatTargetPosition = target => World.Replicas.Objects.TryGetValue(target, out var found) &&
                                (found.BaseState.Flags & GoldObjectLifecycleLayout.InactiveFlag) == 0
                                    ? SacredNetworkPosition.ToTiles(found.BaseState) : null,
                            CombatMotionLoader = motion => _assets.LoadNetworkCombatMotionAsync(asset.ModelName, motion, cancellation),
                            CombatMotionAvailable = motion => _assets.HasNetworkCombatMotion(asset.ModelName, motion) };
                }, cancellation));
            }
            if (entry.Actor is { } presentation)
            {
                presentation.Update(creature, World.OutdoorHeight(creature.BaseState), deltaSeconds);
                presentation.QueueAnimation(_animationBatch);
            }
        }
        _animationBatch.Run(deltaSeconds);
        foreach (var creature in creatures)
        {
            var reference = creature.BaseState.ObjectReference;
            _actors.TryGetValue(reference, out var entry);
            entry.Actor?.CompleteAnimation();
            var conversationFacing = false;
            if (entry.Actor is not null && Conversations is { } conversations)
            {
                if (conversations.States.GetValueOrDefault(reference) is { NeedsAnimation: true } waiting)
                    entry.Actor.ConversationTiming = NetworkNpcAnimationSpeed.Resolve(_assets, World.Replicas, creature, entry.Actor.ConversationMotionFactor(waiting));
                conversationFacing = entry.Actor.ApplyConversation(conversations.States.GetValueOrDefault(reference), conversations);
            }
            if (!creature.IsDead && entry.Actor is not null && _assets.GetItem(creature.BaseState.ObjectType) is { } overheadItem)
            {
                var template = _assets.CreatureTemplates?.ResolveTemplate(overheadItem);
                var overhead = Conversations?.OverheadState(creature, World.LocalHero) ?? SacredNetworkNpcPresentation.OverheadState(creature, World.LocalHero);
                overheads.Add(new(reference, entry.Actor.Model, overhead, overheadItem.ModelDesc.HasNpcDialogMarker,
                    template?.Class == SacredCreatureClass.Animal));
                if (!conversationFacing && !entry.Actor.Moving && creature.Values is { CurrentHitPoints: > 0 } &&
                    SacredNpcClientRules.FacesLocalHero(reference, overhead, template?.Class == SacredCreatureClass.Horse))
                    entry.Actor.FaceLocalHero(World.LocalPosition);
            }
        }
        _scene.NpcOverheads.Actors = overheads.ToArray();
        if (DeathEffects is { } corpseEffects)
            _deaths.CollectDisappearance(creatures, World.PlayerSlot, ParticleQuality, FxActor, corpseEffects);
        _combat.Update(World.Replicas, _scene, _actors, deltaSeconds);
        _worldText.Update(World.Replicas, _assets.Resources, _scene, World.OutdoorHeightAt, deltaSeconds);
        _hudNotices.Update(World.Replicas, _assets.Resources, _scene, deltaSeconds);
        _projectiles.Update(World, GameplayObjects.BowProjectiles, _actors, deltaSeconds);
        if (_assets.CreatureMarkerBalance is { } markerTuning && _assets.CreatureBalance is { } levelTuning)
            _markers.Update(World.Replicas, World.PlayerSlot, _actors.Select(entry =>
            {
                var current = World.Replicas.Objects.GetValueOrDefault(entry.Key);
                if (current is null || current.InstanceVersion != entry.Value.Actor.InstanceVersion ||
                    _assets.GetItem(current.BaseState.ObjectType) is not { } item) return null;
                var model = entry.Value.Actor.Model;
                return new WorldCreatureMarkerActor(current,
                    GameplayProjectileCoordinates.ToTiles(GameplayProjectileCoordinates.FromScene(model.RenderPosition)),
                    // Match the interpolated actor's ground height, rather than the packet's path endpoint.
                    GameplayProjectileCoordinates.FromScene(new(0, 0, model.GroundPlaneZ)).Z,
                    item.ModelDesc.BlockRadius, _hoveredCreature == entry.Key);
            }).OfType<WorldCreatureMarkerActor>(), markerTuning, levelTuning, World.Replicas.Session.Difficulty, deltaSeconds);
        var next = _actors.Where(entry => entry.Key != World.PlayerSlot && World.Replicas.Objects.TryGetValue(entry.Key, out var actor) &&
                (actor.BaseState.Flags & GoldObjectLifecycleLayout.InactiveFlag) == 0 && !_deaths.IsHidden(actor))
            .OrderBy(entry => entry.Key).Select(entry => entry.Value.Actor.Model)
            .Concat(_projectiles.Models).ToArray();
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
        GameplayObjects.Dispose();
        World.Dispose();
        _stop.Cancel();
        var pending = _pending.Values.Select(entry => entry.Load).ToArray();
        foreach (var entry in _pending.Values)
            _ = entry.Load.ContinueWith(task => { _ = task.Exception; }, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        _pending.Clear(); _actors.Clear(); _scene.SetReplicatedModels([]);
        _scene.NpcOverheads.Actors = [];
        _scene.CombatText.Damage = [];
        _scene.CombatText.HudNotices = [];
        _scene.CombatText.ChatLines = [];
        _scene.CombatText.DrawnHudNotices = 0;
        _scene.CombatText.DragDropClears = 0;
        _scene.CombatText.DroppedWorldText = 0;
        _scene.CombatText.WorldText = [];
        _scene.NpcOverheads.DialogIcon = _ => null;
        _ = Task.WhenAll(pending).ContinueWith(task => { _ = task.Exception; _stop.Dispose(); }, CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }
}
