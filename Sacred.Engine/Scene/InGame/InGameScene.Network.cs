using System;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Sacred.Core.World.Sector;
using Sacred.Engine.Multiplayer;
using Sacred.GameplayHost.World;
using Sacred.Inventory.Actors;
using Sacred.Network.Gameplay;
using Sacred.World;
using Sacred.World.Geometry;
using Sacred.World.Networking;
using Sacred.World.Particles;

namespace Sacred.Engine.Scene.InGame;

internal sealed partial class InGameScene
{
    private Task<SacredNetworkWorldBinding>? _networkWorldLoad;
    private NetworkScenePresentation? _networkPresentation;
    private SceneModel? _offlinePlayerModel;
    private SacredGameActor? _offlinePortraitPlayer;
    private SacredGameActor? _offlineEquipmentPlayer;
    private SacredGameActor[] _offlineParty = [];
    private bool _offlineHudVisible;
    private Vector2? _offlineNetworkPosition;
    private string? _networkBindingError;
    private GameServerClientController? _networkClient;
    private NetworkMovementInput? _networkMovement;
    internal bool NetworkConsoleWalkActive { get; set; }
    internal void ConfigureNetworkConnection(GameServerClientController client) => _networkClient = client;
    internal string NetworkMovementStatus => _networkMovement?.Status ?? "movement waits for the local actor";
    internal void NavigateNetworkTo(Vector2 target)
    {
        if (!NetworkWorldReady || !NetworkWorld.CanNavigateSector(NetworkWorld.LocalHero.BaseState.Sector) || _networkMovement is null) throw new InvalidOperationException("Wait for the current sector's native initialization before navigating.");
        _networkMovement.NavigateTo(target);
    }
    internal void StopNetworkMovement() => _networkMovement?.Stop();
    private void UpdatePausedNetworkMovement(float deltaSeconds)
    {
        UpdateNetworkBinding(deltaSeconds, allowMovementInput: false);
        _scene.Hud!.DayFraction = _worldLighting.DayFraction;
        if (_networkPresentation is not null) UpdateWeather(deltaSeconds);
    }
    internal Vector2 OfflineSavePosition => _offlineNetworkPosition ?? PlayerWorldPosition;
    internal string? NetworkBindingError => _networkBindingError;
    internal bool NetworkWorldReady => _networkPresentation is { LocalModelReady:true } view && view.World.IsCurrentSession;
    internal SacredNetworkWorldBinding NetworkWorld => _networkPresentation?.World ?? throw new InvalidOperationException("Network world is not bound.");

    internal Vector2[] FindNetworkWalkRoute(Vector2 target)
    {
        var world=NetworkWorld;
        if(!NetworkWorldReady || !world.CanNavigateSector(world.LocalHero.BaseState.Sector) || world.LocalHero.BaseState.Surface!=0) throw new InvalidOperationException("Console walking requires initialized outdoor network sectors.");
        var collision=new WorldCollisionResolver(_worldStreamer,()=>null,_doors.States)
            { VisibleWorldProvider = () => world.VisibleWorld, SectorReadyProvider = world.CanNavigateSector };
        var finder=new WorldClickPathFinder(collision);
        if(!finder.TryFindRoute(world.LocalPosition,target,_camera.Zoom,out var route)) throw new InvalidOperationException("No authored walk route to that target.");
            if(route.Any(point => !world.TryGetMovementPosition(point, out _)))
            throw new NotSupportedException("Console walking requires loaded, confirmed terrain along its route.");
        return route.ToArray();
    }
    private VisibleWorld PresentedWorld => _networkPresentation?.World.VisibleWorld ?? _worldStreamer.VisibleWorld;
    internal string NetworkWorldStatus => _networkPresentation is { } view
        ? $"bound {view.World.Sectors.Count} native sectors, {view.ModelCount} creature models ({view.AnimatedCount} animated); local model ready {view.LocalModelReady}; terrain loading {view.World.VisibleWorld.LoadingSectors}, error {view.World.TerrainError ?? "none"}; native sector work pending {view.World.PendingNativeSectorWork}; active patches {view.World.SectorPatches.Active.Count}, local enter notifications {view.World.SectorPatches.EnterNotificationCount}; {NetworkMovementStatus}"
        : _networkWorldLoad is not null ? "loading confirmed network sectors" : _networkBindingError ?? "unbound";

    internal string BindNetworkWorld(GoldWorldReplica replicas)
    {
        if (_networkPresentation is not null || _networkWorldLoad is not null) throw new InvalidOperationException("A network world is already bound or loading.");
        _networkBindingError = null;
        _networkWorldLoad = SacredNetworkWorldBinding.LoadAsync(_worldArchive,replicas);
        return "loading confirmed native sectors for scene binding";
    }

    private void UpdateNetworkBinding() => UpdateNetworkBinding(1.0f / 60);
    private void UpdateNetworkBinding(float deltaSeconds) => UpdateNetworkBinding(deltaSeconds, allowMovementInput: true);
    private void UpdateNetworkBinding(float deltaSeconds, bool allowMovementInput)
    {
        if (_networkWorldLoad is { IsCompleted:true } load)
        {
            _networkWorldLoad = null;
            try
            {
                var world = load.GetAwaiter().GetResult();
                if (world.IsCurrentSession)
                {
                    _networkPresentation = new(world,_assets,_scene)
                    { GameplayObjects = new SacredNetworkObjectWorld(world, _assets, _networkClient, EngineLog.WriteLine) };
                    _offlinePlayerModel = _scene.Models[0];
                    _offlinePortraitPlayer = _scene.Portraits.Player;
                    _offlineEquipmentPlayer = _scene.EquipmentUi.Actor;
                    _offlineParty = _scene.Portraits.Party.Members.ToArray();
                    _offlineHudVisible = _scene.Hud!.Visible;
                    _offlineNetworkPosition = PlayerWorldPosition;
                    _scene.Portraits.ClearPlayer(); _scene.Hud.Visible = false; _scene.EquipmentUi.ClearActor();
                    _scene.Portraits.Party.Clear();
                    _inputController.SuspendGameplayInput();
                    Teleport(world.LocalPosition);
                    _scene.Indoor.ActiveGroup = null; // The currently supported network actor surface is outdoor zero.
                    _doors.BindGameplayWorld(_networkPresentation.GameplayObjects);
                    if (_networkClient is { } client) _networkMovement = new(_camera, world, _worldStreamer, client, _window.Input, _doors)
                    { ControllerInteractionPressed = _inputController.ControllerInteractionPressed };
                    EngineLog.WriteLine($"Network scene bound: player {world.PlayerSlot}, {world.Sectors.Count} authored sectors, {world.WorldObjects.Count()} world replicas; local movement awaits native sending.");
                }
            }
            catch (Exception error) { _networkBindingError = "Network scene binding failed: " + error.Message; EngineLog.WriteLine(_networkBindingError); }
        }
        if (_networkPresentation is not { } view) return;
        if (!view.World.IsCurrentSession)
        {
            _doors.BindGameplayWorld(null);
            view.Dispose(); _networkPresentation = null;
            _networkMovement?.Stop(); _networkMovement = null;
            _window.SetHandCursor(false);
            if (_offlinePlayerModel is { } previous) _scene.SetModel(0,previous);
            _scene.Portraits.ClearPlayer();
            if (_offlinePortraitPlayer is { } actor) _scene.Portraits.BindPlayer(actor);
            _scene.Portraits.Party.Clear();
            foreach (var member in _offlineParty) _scene.Portraits.Party.Add(member);
            _offlineParty = [];
            _scene.EquipmentUi.ClearActor();
            if (_offlineEquipmentPlayer is { } equipmentActor) _scene.EquipmentUi.Bind(equipmentActor);
            _offlineEquipmentPlayer = null;
            _worldLighting.SetNetworkDayFraction(null);
            _weather.Clear(); _sectorWeather.Clear(); _weatherEffects.Clear(); _weatherPreview = false;
            _weatherNetworkRevision = -1;
            _camera.SetShakeOffset(Vector2.Zero);
            _particles.ObjectEffects.Clear();
            _particles.ProjectileEffects.Clear();
            _particles.ActorEffects.Clear();
            _particles.CreatureEffects.Clear();
            _scene.Hud!.Visible = _offlineHudVisible;
            _scene.Hud.WeaponSets = null;
            _scene.Hud.SelectedWeaponSlot = 0;
            _scene.Hud.SelectedCombatArtSlot = 0;
            _offlinePlayerModel = null;
            _offlinePortraitPlayer = null;
            if (_offlineNetworkPosition is { } position) Teleport(position);
            _offlineNetworkPosition = null;
            EngineLog.WriteLine("Network scene detached: session ended.");
            return;
        }
        view.LocalLocomotionScale = 1;
        if (_networkMovement is null || !view.LocalModelReady || !view.World.CanNavigateSector(view.World.LocalHero.BaseState.Sector) || NetworkConsoleWalkActive)
        {
            if (NetworkConsoleWalkActive) _networkMovement?.Stop();
            else _networkMovement?.Suspend();
            var position=view.World.LocalPosition;
            var height=view.World.OutdoorHeight(view.World.LocalHero.BaseState);
            _camera.CenterOnTile(position.X,position.Y);
            _camera.SetSurfaceHeight(height);
            _scene.Debug.ActorTerrainHeight=height;
        }
        else
        {
            if (_scene.Portraits.Player?.Movement is { } movement)
            {
                _camera.WalkingSpeed = movement.WalkTilesPerSecond;
                _camera.RunningSpeed = movement.RunTilesPerSecond;
            }
            if (!allowMovementInput || _scene.EscapeMenu!.IsOpen) _networkMovement.Stop();
            _networkMovement.Update(_window.Input, Renderer.RenderWidth, Renderer.RenderHeight, Renderer.OutputToRender,
                deltaSeconds, allowMovementInput && !_scene.EscapeMenu!.IsOpen && !_window.Input.UiWantsKeyboard);
            view.LocalLocomotionScale = _camera.LocomotionAnimationSpeed;
        }
        _window.SetHandCursor(_networkMovement?.HasObjectHover == true);
        view.Update(deltaSeconds);
        var castingEffects = view.GameplayObjects.CombatArtEffects?.Entries.Values
            .Select(effect => view.TryGetFxAttachment(effect.Owner, effect.Bone, out var point)
                ? new WorldAttachedParticleEffect(effect.Generation, effect.Type, effect.Preset,
                    GameplayProjectileCoordinates.ToTiles(point), point.Z, effect.Age, effect.ClockScale) : null)
            .OfType<WorldAttachedParticleEffect>() ?? [];
        var statusEffects = view.GameplayObjects.StatusEffects?.Entries.Values
            .Where(effect => effect.VisualAvailable)
            .Select(effect => view.TryGetFxAttachment(effect.Owner, effect.Bone, out var point)
                ? new WorldAttachedParticleEffect(effect.Generation, effect.Type, effect.Preset,
                    GameplayProjectileCoordinates.ToTiles(point), point.Z, effect.Age)
                    { DurationSeconds = float.IsFinite(effect.Duration) ? effect.Duration : null }
                : null).OfType<WorldAttachedParticleEffect>() ?? [];
        _particles.ActorEffects.Synchronize(castingEffects.Concat(statusEffects));
        _particles.CreatureEffects.Synchronize(view.World.Replicas.AttachedFx.Entries.Values, view.ParticleAttachment);
        _particles.ProjectileEffects.Synchronize(view.GameplayObjects.BowProjectiles?.Entries.Values
            .Where(projectile => projectile.IsBfg && !projectile.Retired && projectile.Flight is { Active: true })
            .Select(projectile =>
            {
                var flight = projectile.Flight!;
                var position = GameplayProjectileCoordinates.ToTiles(flight.Position);
                var ground = GameplayProjectileCoordinates.FromScene(new(0, 0, view.World.OutdoorHeightAt(position))).Z;
                return new WorldSpriteFlareEffect(projectile.Request.Reference, projectile.Request.Generation, projectile.Type,
                    0, position, flight.Position.Z, ground, flight.Age);
            }) ?? []);
        _particles.ObjectEffects.Synchronize(view.World.WorldObjects, reference =>
            view.GameplayObjects.Objects.TryGetValue(reference, out var wearer)
                ? new WorldObjectFxAttachment(new(wearer.Position.X, wearer.Position.Y),
                    wearer.Drop?.Height ?? view.World.OutdoorHeight(wearer.Replica!.BaseState) /
                        (WorldModelPose.Scale * WorldModelPose.CameraProjection.M33),
                    wearer.Definition.ModelDesc.BlockRadius)
                    { Orientation = wearer.Drop?.Orientation ?? Matrix4x4.CreateRotationZ(wearer.RotationRadians),
                        InstanceVersion = wearer.InstanceVersion,
                        BoneAttachment = wearer.Replica?.Creature is not null
                            ? bone => view.ParticleAttachment(reference, bone) : null }
                : null);
        _worldLighting.SetNetworkDayFraction(view.World.Replicas.Calendar.DayFraction);
        var positionForZone = view.World.LocalPosition;
        var coord = new SectorCoord((int)MathF.Floor(positionForZone.X / Sector.TileCount), (int)MathF.Floor(positionForZone.Y / Sector.TileCount));
        var zone = view.World.VisibleWorld.Sectors.FirstOrDefault(sector => sector.Coord == coord)?.Zone ?? WorldZone.Outdoors;
        _worldLighting.Update(0, _scene.Lighting, zone);
    }

    private void DisposeNetworkBinding()
    {
        _networkMovement?.Stop();
        _doors.BindGameplayWorld(null);
        _networkPresentation?.Dispose();
        if (_networkWorldLoad is { } load)
            _ = load.ContinueWith(t => { _ = t.Exception; }, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,TaskScheduler.Default);
    }
}
