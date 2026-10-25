using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Sacred.Core.Pak.Creature;
using Sacred.Core.Pak.Items;
using Sacred.Engine.Animation;
using Sacred.Engine.Assets;
using Sacred.Engine.Scene;
using Sacred.Engine.Scene.InGame;
using Sacred.GameplayHost.World;
using Sacred.Granny.Animation;
using Sacred.Granny.Assets;
using Sacred.Network.Gameplay;
using Sacred.World.Geometry;
using Sacred.World.Networking;

namespace Sacred.Engine.Multiplayer;

/// <summary>One independent animation and equipped skeleton per native creature reference.</summary>
internal sealed class NetworkActorPresentation
{
    private readonly CharacterAnimationState? _animation;
    private readonly string _motionSource;
    private Vector2 _lastPosition;
    private float _rotation;
    private float _pendingHeight;
    private uint? _lastFacing;
    private GoldMovementEvent? _lastMovement;
    private GoldMovementBody? _movementBody;
    private long? _lastPositionVersion;
    private long? _lastInstanceVersion;
    private long? _conversationVersion;
    private readonly SacredNetworkMovementPlayback _motion = new();
    private uint _locomotionAction = 2;
    private NetworkCombatAnimation? _combat;
    private NetworkCreatureObjectAnimation? _objectAnimation;
    private NetworkHitReaction? _hitReaction;
    private NetworkDeathAnimation? _death;
    public bool DeathPoseReady => _death?.PoseReady == true;
    public void ReceiveDeath(GoldReplicatedObject actor, float age, bool corpse)
    {
        if (_animation is null || CombatMotionLoader is null) return;
        _combat?.Interrupt(actor); _combat = null; _objectAnimation = null; _hitReaction = null;
        _animation.ClearHitReaction(); _conversationVersion = null;
        _death = new(_animation, actor, age, corpse, CombatMotionLoader, CombatMotionAvailable ?? (_ => false));
    }
    public void ReceiveHitReaction(GoldReplicatedObject actor, SacredCreatureClass creatureClass, float age)
    {
        if (actor.IsDead || _animation is null || CombatMotionLoader is null) return;
        _hitReaction ??= new(_animation, CombatMotionLoader);
        _hitReaction.Receive(actor, creatureClass, age);
    }
    public void ReceiveObjectAnimation(GoldObjectAnimationEvent request, GoldReplicatedObject actor, float age, Func<bool> complete)
    {
        if (actor.IsDead || _animation is null || CombatMotionLoader is null) return;
        _combat ??= new(_animation, CombatMotionLoader) { MotionSource = _motionSource };
        _combat.Interrupt(actor);
        _objectAnimation ??= new(_animation, CombatMotionLoader);
        _objectAnimation.Receive(request, actor, age, complete);
    }
    private readonly IReadOnlyList<GrnBoneDiagnostics> _attachmentBones;
    public Func<uint, Task<GrnAnimationClip?>>? CombatMotionLoader { get; init; }
    public Func<uint, bool>? CombatMotionAvailable { get; init; }
    public Func<GoldReplicatedObject, GameplayCombatArtAnimation?>? CombatArtSelector { get; init; }
    public Action<GoldReplicatedObject, float>? CombatArtTiming { get; init; }
    public Func<uint, Vector2?>? CombatTargetPosition { get; init; }
    public float GlobalAnimationScale { get; init; } = 1;
    public void ContinueCombatFrom(NetworkActorPresentation previous)
    {
        if (previous._combat is null || _animation is null || CombatMotionLoader is null) return;
        _combat = new(_animation, CombatMotionLoader) { MotionSource = _motionSource };
        _combat.ContinueFrom(previous._combat);
    }
    public bool IsLocal { get; init; }
    public Func<Vector2, float>? TerrainHeight { get; init; }
    public SceneModel Model { get; }
    public long? InstanceVersion => _lastInstanceVersion;
    public bool Animated => _animation?.AnimatedMesh is not null;
    public bool TryGetFxAttachment(string boneName, out Vector3 position)
    {
        position = default;
        if (_animation?.AnimatedMesh?.Pose is not { IsEvaluated: true } pose) return false;
        Vector3 local;
        if (pose.Data.TryFindBone(boneName, out var index))
            local = pose.Data.Projection.Project(pose.BoneWorldTransforms[index].Translation);
        else
        {
            // Equipped helpers participate in the native wrapper's bone lookup too.
            var helper = _attachmentBones.FirstOrDefault(b => b.Name.Equals(boneName, StringComparison.OrdinalIgnoreCase));
            if (helper?.AnimationBoneName is not { } parent ||
                !GrnPoseQueries.TryTransformRigidPoint(pose, parent, helper.Position, out local)) return false;
        }
        position = GameplayProjectileCoordinates.FromScene(Vector3.Transform(local, Model.Transform));
        return true;
    }
    public bool Moving { get; private set; }
    public float? MeleeImpactTime(GoldCreatureAttackEvent attack) =>
        _combat?.MeleeImpactTime is { } timing && ReferenceEquals(timing.Attack, attack) ? timing.Seconds : null;
    public bool TryGetBowRelease(GoldCreatureAttackEvent attack, out Vector3 source, out float age, string boneName = "Bip01 L Hand")
    {
        source = default; age = 0;
        if (_combat?.BowRelease is not { } release || !ReferenceEquals(release.Attack, attack) ||
            _animation?.AnimatedMesh is not { } mesh) return false;
        // Evaluate only the release pose. A delayed model load must not use a later idle hand position.
        var pose = new GrnPoseEvaluator(mesh.Pose.Data, release.Clip).EvaluateClamped(release.SampleTime);
        Vector3 local;
        if (mesh.Pose.Data.TryFindBone(boneName, out var bone))
            local = pose.Data.Projection.Project(pose.BoneWorldTransforms[bone].Translation);
        else
        {
            // Rigid equipment helpers remain in their retargeted GRN slice, rather
            // than becoming character skeleton bones. Use the same rigid palette.
            var helper = _attachmentBones.FirstOrDefault(b => b.Name.Equals(boneName, StringComparison.OrdinalIgnoreCase));
            if (helper?.AnimationBoneName is not { } parent ||
                !GrnPoseQueries.TryTransformRigidPoint(pose, parent, helper.Position, out local)) return false;
        }
        source = GameplayProjectileCoordinates.FromScene(Vector3.Transform(local, Model.Transform));
        age = release.Age;
        return true;
    }
    public bool TryGetBowTarget(out Vector3 target)
    {
        target = default;
        if (_animation?.AnimatedMesh is not { } mesh || mesh.Playback.LastRequest is not { } request) return false;
        var data = mesh.Pose.Data;
        // Gold 0x44CBB0 uses bone-name entry 3, then entry 0, with no random offset for bow aim.
        if (!data.TryFindBone("Bip01 Neck", out var bone) && !data.TryFindBone("__Root", out bone)) return false;
        var evaluator = new GrnPoseEvaluator(data, request.Animation);
        var pose = request.Blend is { } blend ? evaluator.EvaluateBlended(request.TimeSeconds, request.TimeMode, blend)
            : request.TimeMode == GrnPoseTimeMode.Clamp ? evaluator.EvaluateClamped(request.TimeSeconds) : evaluator.Evaluate(request.TimeSeconds);
        target = GameplayProjectileCoordinates.FromScene(Vector3.Transform(
            data.Projection.Project(pose.BoneWorldTransforms[bone].Translation), Model.Transform));
        return true;
    }
    public float LocomotionPlaybackSpeed { get; set; } = 1;
    public Func<GoldReplicatedObject, uint, int, float>? LocomotionSpeedResolver { get; init; }
    public void QueueAnimation(CharacterAnimationBatch batch)
    {
        if (_animation is not null) batch.Add(_animation, LocomotionPlaybackSpeed);
    }
    public void CompleteAnimation()
        => Model.SetPose(new(_lastPosition, _pendingHeight), new(0, 0, _rotation), _lastPosition, _pendingHeight);
    public float ConversationMotionFactor(SacredNetworkNpcConversationState state) => _animation?.NpcAnimation(state.Motion)?.PlaybackSpeedMultiplier ?? 1;
    public SacredNpcAnimationTiming? ConversationTiming { get; set; }
    public bool ApplyConversation(SacredNetworkNpcConversationState? state, SacredNetworkNpcConversations conversations)
    {
        if (_death is not null || _combat?.Active == true || _objectAnimation?.Active == true) { _conversationVersion = null; return true; }
        if (state?.ActionStage is 101 or 102)
        {
            var replacing = _conversationVersion is not null && state.Version != _conversationVersion;
            if (state.NeedsAnimation && state.Version != _conversationVersion && Animated && ConversationTiming is { } timing &&
                _animation!.NpcAnimation(state.Motion) is { } clip && conversations.StartAnimation(state.Reference, state.Version, clip.DurationSeconds / timing.PlaybackSpeed))
            {
                // Native 0x593320 promotes the previous target when interrupted. Starting
                // through idle first discards its clip clock and produces a visible jump.
                _animation.PlayNpcAnimation(clip, timing);
                _conversationVersion = state.Version;
                EngineLog.WriteLine($"Network NPC conversation: ref {state.Reference}; {state.Motion}; clip {clip.Name}, {clip.DurationSeconds:0.###} seconds; speed {timing.PlaybackSpeed:0.###}, blend {timing.BlendSeconds:0.###} seconds.");
            }
            else if (replacing)
            { _animation?.StopNpcAnimation(); _conversationVersion = null; }
            if (state.FacingPosition is { } target) FaceLocalHero(target);
            return true;
        }
        if (_conversationVersion is not null) { _animation?.StopNpcAnimation(); _conversationVersion = null; }
        if (state?.FacingPosition is { } receiverTarget) FaceLocalHero(receiverTarget);
        return state?.FacingPosition is not null;
    }
    public void FaceLocalHero(Vector2 hero)
    {
        var direction = hero - _lastPosition;
        if (direction.LengthSquared() < .0000001f) return;
        _rotation = SacredNetworkFacing.ToModelRadians(SacredNetworkFacing.FromTileDirection(direction));
        Model.SetPose(Model.Position, new(0, 0, _rotation), Model.DepthAnchor, Model.GroundPlaneZ);
    }

    public NetworkActorPresentation(uint reference, PlayerCharacterAsset asset, PlayerCharacterAnimations? animations,
        ItemsPakEntry item, Vector2 position)
    {
        _motionSource = asset.ModelName;
        var mesh = asset.Model.Mesh ?? throw new InvalidOperationException("Creature model has no geometry.");
        _attachmentBones = asset.Model.Diagnostics?.Slices.Skip(1).SelectMany(slice => slice.Bones).ToArray() ?? [];
        _lastPosition = position;
        _rotation = WorldModelPose.RotationRadians(item.ModelDesc.Angle3D);
        Model = new($"Network ref {reference}, resource {item.ItemIndex}", mesh, new(position, 0),
            new(0, 0, _rotation), WorldModelPose.Scale, asset.TextureAliases, asset.EquipmentEffects,
            sourceOriginOffset: asset.Model.Diagnostics?.SourceOriginOffset ?? Vector3.Zero,
            blockRadius: item.ModelDesc.BlockRadius);
        CreatureModelPlacement.Apply(Model, asset.Model);
        if (animations is not null)
        {
            _animation = new(asset.Model, animations, mesh, asset.EquipmentEffects);
            if (_animation.AnimatedMesh is { } animated)
            {
                Model.SetAnimatedMesh(animated);
                // Animation evaluates the root in sequence space already.
                Model.SetSourceTransform(Matrix4x4.Identity);
            }
        }
    }

    public void Update(GoldReplicatedObject actor, float height, float deltaSeconds)
    {
        if (actor.IsDead)
        {
            if (_lastInstanceVersion != actor.InstanceVersion)
            {
                var facing = actor.FacingBits ?? actor.BaseState.Word28;
                if (float.IsFinite(BitConverter.UInt32BitsToSingle(facing)))
                    _rotation = SacredNetworkFacing.ToModelRadians(facing);
            }
            _death?.Update(deltaSeconds, GlobalAnimationScale);
            Moving = false;
            _lastPosition = SacredNetworkPosition.ToTiles(actor.BaseState);
            _pendingHeight = TerrainHeight?.Invoke(_lastPosition) ?? height;
            _lastInstanceVersion = actor.InstanceVersion;
            return;
        }
        if (_death is not null) { _death = null; _animation?.StopNpcAnimation(); }
        _hitReaction?.Update(deltaSeconds, GlobalAnimationScale);
        _objectAnimation?.Update(actor, deltaSeconds, GlobalAnimationScale);
        if (_combat is null && _animation is not null && CombatMotionLoader is not null)
            _combat = new(_animation, CombatMotionLoader) { MotionSource = _motionSource };
        if (_combat is not null)
        {
            _combat.GlobalPlaybackScale = GlobalAnimationScale;
            _combat.SelectArtAnimation = CombatArtSelector;
            _combat.ArtTiming = CombatArtTiming;
            _combat.Update(actor, deltaSeconds);
        }
        if (actor.SpecialMove is { } special)
        {
            var target = CombatTargetPosition?.Invoke(special.TargetReference) ??
                new Vector2(special.TargetX, special.TargetY) / SacredNetworkPosition.NativeUnitsPerTile;
            if (target != _lastPosition) FaceLocalHero(target);
        }
        if (actor.Cast is { } cast)
        {
            var target = CombatTargetPosition?.Invoke(cast.TargetReference) ??
                new Vector2(cast.TargetX, cast.TargetY) / SacredNetworkPosition.NativeUnitsPerTile;
            if (target != _lastPosition) FaceLocalHero(target);
        }
        var reported = SacredNetworkPosition.ToTiles(actor.BaseState);
        var position = IsLocal ? reported : _motion.Update(actor, deltaSeconds);
        var moving = IsLocal ? actor.Movement is not null : _motion.Moving;
        Moving = moving;
        var direction = _motion.Direction;
        if (!IsLocal && moving && direction != Vector2.Zero)
            _rotation = CreatureModelPose.RotationRadians(direction);
        else if ((actor.FacingBits ?? actor.BaseState.Word28) is var facing &&
                 (facing != _lastFacing || actor.PositionVersion != _lastPositionVersion || actor.InstanceVersion != _lastInstanceVersion))
        {
            // Full snapshots derive this heading from physical +0x70; stop/position packets carry the angle word.
            var degrees = BitConverter.UInt32BitsToSingle(facing);
            if (float.IsFinite(degrees)) _rotation = SacredNetworkFacing.ToModelRadians(facing);
        }
        _lastFacing = actor.FacingBits ?? actor.BaseState.Word28;
        _lastPositionVersion = actor.PositionVersion;
        _lastInstanceVersion = actor.InstanceVersion;
        if (!ReferenceEquals(actor.Movement, _lastMovement))
        {
            _lastMovement = actor.Movement;
            _movementBody = actor.Movement?.Body;
        }
        var action = _movementBody?.Word1F8 ?? actor.Creature!.Word1F8;
        if (action is 1 or 2) _locomotionAction = action;
        if (LocomotionSpeedResolver is { } resolveSpeed)
            LocomotionPlaybackSpeed = resolveSpeed(actor, _locomotionAction, _motion.Speed);
        _animation?.SetLocomotionState(moving ? _locomotionAction == 1 ? CharacterAnimationStateId.Walk : CharacterAnimationStateId.Run
            : actor.Creature!.IsInFightMode ? CharacterAnimationStateId.FightingIdle : CharacterAnimationStateId.Idle);
        height = TerrainHeight?.Invoke(position) ?? height;
        _pendingHeight = height;
        _lastPosition = position;
    }
}
