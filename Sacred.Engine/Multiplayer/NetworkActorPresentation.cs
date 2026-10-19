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
    private Vector2 _lastPosition;
    private float _rotation;
    private float _groundOffset;
    private float _pendingHeight;
    private uint? _lastFacing;
    private GoldMovementEvent? _lastMovement;
    private GoldMovementBody? _movementBody;
    private long? _lastPositionVersion;
    private long? _conversationVersion;
    private readonly SacredNetworkMovementPlayback _motion = new();
    private uint _locomotionAction = 2;
    private NetworkCombatAnimation? _combat;
    private readonly IReadOnlyList<GrnBoneDiagnostics> _attachmentBones;
    public Func<uint, Task<GrnAnimationClip?>>? CombatMotionLoader { get; init; }
    public float GlobalAnimationScale { get; init; } = 1;
    public void ContinueCombatFrom(NetworkActorPresentation previous)
    {
        if (previous._combat is null || _animation is null || CombatMotionLoader is null) return;
        _combat = new(_animation, CombatMotionLoader);
        _combat.ContinueFrom(previous._combat);
    }
    public bool IsLocal { get; init; }
    public Func<Vector2, float>? TerrainHeight { get; init; }
    public SceneModel Model { get; }
    public bool Animated => _animation?.AnimatedMesh is not null;
    public bool Moving { get; private set; }
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
    public void QueueAnimation(CharacterAnimationBatch batch)
    {
        if (_animation is not null) batch.Add(_animation, LocomotionPlaybackSpeed);
    }
    public void CompleteAnimation()
        => Model.SetPose(new(_lastPosition, _pendingHeight + _groundOffset), new(0, 0, _rotation), _lastPosition, _pendingHeight);
    public float ConversationMotionFactor(SacredNetworkNpcConversationState state) => _animation?.NpcAnimation(state.Motion)?.PlaybackSpeedMultiplier ?? 1;
    public SacredNpcAnimationTiming? ConversationTiming { get; set; }
    public bool ApplyConversation(SacredNetworkNpcConversationState? state, SacredNetworkNpcConversations conversations)
    {
        if (_combat?.Active == true) { _conversationVersion = null; return true; }
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
        var mesh = asset.Model.Mesh ?? throw new InvalidOperationException("Creature model has no geometry.");
        _attachmentBones = asset.Model.Diagnostics?.Slices.Skip(1).SelectMany(slice => slice.Bones).ToArray() ?? [];
        _lastPosition = position;
        _rotation = WorldModelPose.RotationRadians(item.ModelDesc.Angle3D);
        _groundOffset = asset.Model.Diagnostics?.WholeModelBounds is { } bounds ? -bounds.Min.Z * WorldModelPose.Scale : 0;
        Model = new($"Network ref {reference}, resource {item.ItemIndex}", mesh, new(position, 0),
            new(0, 0, _rotation), WorldModelPose.Scale, asset.TextureAliases, asset.EquipmentEffects,
            blockRadius: item.ModelDesc.BlockRadius);
        Model.SetModelProjection(WorldModelPose.CameraProjection);
        if (animations is not null)
        {
            _animation = new(asset.Model, animations, mesh, asset.EquipmentEffects);
            if (_animation.AnimatedMesh is { } animated) Model.SetAnimatedMesh(animated);
        }
    }

    public void Update(GoldReplicatedObject actor, float height, float deltaSeconds)
    {
        if (_combat is null && _animation is not null && CombatMotionLoader is not null)
            _combat = new(_animation, CombatMotionLoader);
        if (_combat is not null)
        {
            _combat.GlobalPlaybackScale = GlobalAnimationScale;
            _combat.Update(actor, deltaSeconds);
        }
        if (actor.SpecialMove is { } special)
        {
            var target = new Vector2(special.TargetX, special.TargetY) / SacredNetworkPosition.NativeUnitsPerTile;
            if (target != _lastPosition) FaceLocalHero(target);
        }
        var reported = SacredNetworkPosition.ToTiles(actor.BaseState);
        var position = IsLocal ? reported : _motion.Update(actor, deltaSeconds);
        var moving = IsLocal ? actor.Movement is not null : _motion.Moving;
        Moving = moving;
        var direction = _motion.Direction;
        if (!IsLocal && moving && direction != Vector2.Zero)
            _rotation = -(MathF.Atan2(direction.Y, direction.X) + MathF.PI / 4);
        else if ((actor.FacingBits ?? actor.BaseState.Word28) is var facing &&
                 (facing != _lastFacing || actor.PositionVersion != _lastPositionVersion))
        {
            // Full snapshots derive this heading from physical +0x70; stop/position packets carry the angle word.
            var degrees = BitConverter.UInt32BitsToSingle(facing);
            if (float.IsFinite(degrees)) _rotation = SacredNetworkFacing.ToModelRadians(facing);
        }
        _lastFacing = actor.FacingBits ?? actor.BaseState.Word28;
        _lastPositionVersion = actor.PositionVersion;
        if (!ReferenceEquals(actor.Movement, _lastMovement))
        {
            _lastMovement = actor.Movement;
            _movementBody = actor.Movement?.Body;
        }
        var action = _movementBody?.Word1F8 ?? actor.Creature!.Word1F8;
        if (action is 1 or 2) _locomotionAction = action;
        _animation?.SetLocomotionState(moving ? _locomotionAction == 1 ? CharacterAnimationStateId.Walk : CharacterAnimationStateId.Run
            : CharacterAnimationStateId.Idle);
        height = TerrainHeight?.Invoke(position) ?? height;
        _pendingHeight = height;
        _lastPosition = position;
    }
}
