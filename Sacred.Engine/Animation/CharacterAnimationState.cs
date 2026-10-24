using System;
using Sacred.Core.Pak.Creature;
using Sacred.Engine.Assets;
using Sacred.Granny.Animation;
using Sacred.Granny.Assets;
using Sacred.Granny.Meshes;
using Sacred.Inventory.Effects;

namespace Sacred.Engine.Animation;

internal enum CharacterAnimationStateId
{
    Idle,
    Walk,
    Run,
    Defend,
    Attack
}

/// <summary>A small immediate-transition state machine for the player character.</summary>
internal sealed class CharacterAnimationState
{
    private const float MinimumPoseIntervalSeconds = 1.0f / 240.0f;

    private readonly GrnAnimatedMesh? _animatedMesh;
    private readonly EquipmentEffectScene? _equipmentEffects;
    private readonly PlayerCharacterAnimations _animations;
    private float _stateTimeSeconds;
    private float _timeSinceLastPose;
    private CharacterAnimationStateId _locomotionState;
    private GrnAnimationClip? _npcAnimation;
    private SacredNpcAnimationTiming _npcTiming;
    private GrnAnimationClip? _blendSource;
    private float _blendSourceTime, _blendSourceSpeed, _blendElapsed, _blendDuration;
    private GrnPoseTimeMode _blendSourceMode;
    private float _idleSpeed = 1;
    private bool _deferEquipmentEffects;
    private bool _initialEquipmentPosePending;

    // The batch owns this state exclusively until its completion barrier. Effects may
    // submit to renderer-owned sinks and therefore stay on the calling thread.
    public void UpdatePoseOnly(float deltaSeconds, float playbackSpeed)
    {
        _deferEquipmentEffects = true;
        _initialEquipmentPosePending = false;
        try { Update(deltaSeconds, playbackSpeed); }
        finally { _deferEquipmentEffects = false; }
    }

    public void UpdateEquipmentEffects(float deltaSeconds)
    {
        if (_initialEquipmentPosePending)
        {
            _initialEquipmentPosePending = false;
            if (_animatedMesh is not null) _equipmentEffects?.ApplyPose(_animatedMesh);
        }
        if (_animatedMesh is not null && float.IsFinite(deltaSeconds) && deltaSeconds > 0)
            _equipmentEffects?.ApplyPose(_animatedMesh, deltaSeconds);
    }

    public GrnAnimationClip? NpcAnimation(SacredNpcConversationMotion motion) => motion switch
    {
        SacredNpcConversationMotion.Talk => _animations.NpcTalk,
        SacredNpcConversationMotion.AttackReaction => _animations.NpcReactionAttack,
        _ => null
    };

    public void PlayNpcAnimation(GrnAnimationClip clip)
        => PlayNpcAnimation(clip, new(clip.PlaybackSpeedMultiplier, .2f, 1));

    public void PlayNpcAnimation(GrnAnimationClip clip, SacredNpcAnimationTiming timing)
        => PlayNpcAnimation(clip, timing, 0);

    public void PlayNpcAnimation(GrnAnimationClip clip, SacredNpcAnimationTiming timing, float startTimeSeconds)
    {
        BeginBlend(timing.BlendSeconds, timing.PlaybackSpeed / clip.PlaybackSpeedMultiplier);
        _npcAnimation = clip;
        _npcTiming = timing;
        _stateTimeSeconds = startTimeSeconds;
        _timeSinceLastPose = 0;
        _animatedMesh?.SetAnimation(clip);
        if (_animatedMesh is not null) SubmitCurrentPose(startTimeSeconds);
    }

    public void StopNpcAnimation()
    {
        if (_npcAnimation is null) return;
        BeginBlend(_npcTiming.ReturnBlendSeconds, _npcTiming.ActorFactor);
        _idleSpeed = _npcTiming.ActorFactor;
        _npcAnimation = null;
        CurrentState = _locomotionState;
        _stateTimeSeconds = _timeSinceLastPose = 0;
        if (_animatedMesh is null) return;
        // Keep the outgoing talk pose through this switch. SetState's immediate
        // unblended pose would also move equipment effects to idle for one frame.
        _animatedMesh.SetAnimation(AnimationFor(CurrentState));
        SubmitCurrentPose(0);
        _equipmentEffects?.ApplyPose(_animatedMesh);
    }

    private void BeginBlend(float duration, float sourceRate)
    {
        if (_animatedMesh is null) return;
        _blendSource = _animatedMesh.Playback.Animation;
        _blendSourceTime = _stateTimeSeconds;
        _blendSourceSpeed = sourceRate * _blendSource.PlaybackSpeedMultiplier;
        _blendSourceMode = _npcAnimation is null ? GrnPoseTimeMode.Loop : GrnPoseTimeMode.Clamp;
        _blendElapsed = 0; _blendDuration = duration;
    }

    public CharacterAnimationState(
        GrnAsset asset,
        PlayerCharacterAnimations animations,
        Mesh fallbackMesh,
        EquipmentEffectScene? equipmentEffects)
    {
        _animations = animations;
        _equipmentEffects = equipmentEffects;
        CurrentState = CharacterAnimationStateId.Idle;
        _locomotionState = CharacterAnimationStateId.Idle;
        if (asset.Mesh is not null && asset.Skin is not null)
        {
            _animatedMesh = new GrnAnimatedMesh(asset.Mesh, asset.Skin, animations.Idle);
            _animatedMesh.RequestPose(0.0f);
            _fallbackMesh = _animatedMesh.Mesh;
        }
        else
        {
            _fallbackMesh = asset.Mesh ?? fallbackMesh;
        }
    }

    public CharacterAnimationStateId CurrentState { get; private set; }

    private readonly Mesh _fallbackMesh;
    public Mesh Mesh => _animatedMesh?.Mesh ?? _fallbackMesh;
    public GrnAnimatedMesh? AnimatedMesh => _animatedMesh;
    public GrnPose? Pose => _animatedMesh?.Pose;
    private bool _materializeCpuVertices = true;
    public bool MaterializeCpuVertices
    {
        get => _animatedMesh?.MaterializeCpuVertices ?? _materializeCpuVertices;
        set
        {
            if (value && _animatedMesh?.Pose.IsEvaluated == true) _animatedMesh.MaterializeCpuMesh();
            _materializeCpuVertices = value;
            if (_animatedMesh is not null) _animatedMesh.MaterializeCpuVertices = value;
        }
    }

    public void ApplyEquipmentEffectPose()
    {
        if (_animatedMesh is not null)
            _equipmentEffects?.ApplyPose(_animatedMesh);
    }

    public void SetLocomotionState(CharacterAnimationStateId state)
    {
        if (state == CharacterAnimationStateId.Attack)
            throw new ArgumentOutOfRangeException(nameof(state));

        _locomotionState = state;
        if (_npcAnimation is null && CurrentState != CharacterAnimationStateId.Attack)
            SetState(state);
    }

    public void PlayAttack() => SetState(CharacterAnimationStateId.Attack, restart: true);

    public void StopAction()
    {
        if (_npcAnimation is not null) StopNpcAnimation();
        else if (CurrentState == CharacterAnimationStateId.Attack) SetState(_locomotionState);
    }

    public void Update(float deltaSeconds, float locomotionPlaybackSpeed = 1.0f)
    {
        if (_animatedMesh is null || !float.IsFinite(deltaSeconds) || deltaSeconds <= 0.0f)
            return;

        var animationDelta = _npcAnimation is not null ? deltaSeconds * _npcTiming.PlaybackSpeed
            : CurrentState == CharacterAnimationStateId.Idle ? deltaSeconds * _idleSpeed * _animations.Idle.PlaybackSpeedMultiplier
            : (CurrentState is CharacterAnimationStateId.Walk or CharacterAnimationStateId.Run)
            ? deltaSeconds * MathF.Max(0.0f, locomotionPlaybackSpeed) * AnimationFor(CurrentState).PlaybackSpeedMultiplier
            : deltaSeconds;
        if (_blendSource is not null)
        {
            _blendElapsed += deltaSeconds; _blendSourceTime += deltaSeconds * _blendSourceSpeed;
            if (_blendElapsed >= _blendDuration) _blendSource = null;
        }
        _stateTimeSeconds += animationDelta;
        if (_npcAnimation is null && CurrentState == CharacterAnimationStateId.Attack &&
            _stateTimeSeconds >= MathF.Max(_animations.Attack.DurationSeconds, MinimumPoseIntervalSeconds))
        {
            SetState(_locomotionState);
        }

        _timeSinceLastPose += animationDelta;
        if (_timeSinceLastPose >= MinimumPoseIntervalSeconds)
        {
            _timeSinceLastPose %= MinimumPoseIntervalSeconds;
            SubmitCurrentPose(_stateTimeSeconds);
        }
        if (!_deferEquipmentEffects) _equipmentEffects?.ApplyPose(_animatedMesh, deltaSeconds);
    }

    private void SetState(CharacterAnimationStateId state, bool restart = false)
    {
        if (!restart && state == CurrentState)
            return;

        CurrentState = state;
        _blendSource = null;
        _stateTimeSeconds = 0.0f;
        _timeSinceLastPose = 0.0f;
        if (_animatedMesh is null)
            return;

        _animatedMesh.SetAnimation(AnimationFor(state));
        SubmitCurrentPose(0.0f);
        if (!_deferEquipmentEffects) _equipmentEffects?.ApplyPose(_animatedMesh);
        else _initialEquipmentPosePending = true;
    }

    private GrnAnimationClip AnimationFor(CharacterAnimationStateId state) => state switch
    {
        CharacterAnimationStateId.Idle => _animations.Idle,
        CharacterAnimationStateId.Walk => _animations.Walk,
        CharacterAnimationStateId.Run => _animations.Run,
        CharacterAnimationStateId.Defend => _animations.Defend,
        CharacterAnimationStateId.Attack => _animations.Attack,
        _ => throw new ArgumentOutOfRangeException(nameof(state))
    };

    private void SubmitCurrentPose(float time)
    {
        if (_blendSource is { } source && _blendDuration > 0)
            _animatedMesh!.Playback.RequestBlend(time, _npcAnimation is null ? GrnPoseTimeMode.Loop : GrnPoseTimeMode.Clamp,
                _animatedMesh.MaterializeCpuVertices, new(source, _blendSourceTime, _blendElapsed / _blendDuration) { SourceMode = _blendSourceMode });
        else _animatedMesh!.RequestPose(time, _npcAnimation is null ? GrnPoseTimeMode.Loop : GrnPoseTimeMode.Clamp);
    }
}
