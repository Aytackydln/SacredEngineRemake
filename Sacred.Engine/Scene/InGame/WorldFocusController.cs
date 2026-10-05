using System;
using System.Numerics;
using Sacred.Core.World;
using Sacred.Engine.Platform;

namespace Sacred.Engine.Scene.InGame;

/// <summary>Owns one shared mouse/directional target and the narrow stairs approach assist.</summary>
internal sealed class WorldFocusController(DoorSceneController objects, StairsTraversalController stairs)
{
    public const float Range = 3.0f;
    public const float HalfAngleDegrees = 55.0f;
    private Vector2? _lastMouse;
    private Vector2 _direction;
    private Vector2 _lastIntent;
    private bool _directional;
    public Vector2? DebugDirection { get; set; }
    public Vector2? DebugMouseWorld { get; set; }
    public Vector2? DebugMouseScreen { get; set; }
    public WorldFocusTarget? Target { get; private set; }
    public string Status => Target is { } target
        ? $"focus {target.Kind} {target.Id} at {target.Position.X:0.###},{target.Position.Y:0.###}; {(_directional ? "cone" : "mouse")}; cone {HalfAngleDegrees * 2} degrees, range {Range} tiles"
        : "focus none";

    public void Update(InputState input, SacredCamera camera, Vector2 mouseScreen, Vector2 mouseWorld,
        int width, int height, byte surfaceLevel, MovementInputAvailability availability)
    {
        var intent = camera.GetManualDirection(input, availability);
        if (intent.LengthSquared() > 0.000001f) intent = Vector2.Normalize(intent);
        else intent = Vector2.Zero;
        var newManualIntent = intent != Vector2.Zero &&
            (_lastIntent == Vector2.Zero || Vector2.DistanceSquared(intent, _lastIntent) > 0.0001f);
        _lastIntent = intent;
        var mouseChanged = _lastMouse is { } lastMouse && lastMouse != input.MousePosition;
        _lastMouse = input.MousePosition;
        if (DebugDirection is { } debugDirection) { _direction = debugDirection; _directional = true; }
        else if (DebugMouseWorld is not null || DebugMouseScreen is not null) _directional = false;
        else if ((mouseChanged || input.HasPendingLeftClick) && !input.UiWantsMouse) _directional = false;
        else if (newManualIntent) { _direction = intent; _directional = true; }
        if (_directional && intent != Vector2.Zero && DebugDirection is null) _direction = intent;

        if (!_directional && input.UiWantsMouse && DebugMouseWorld is null && DebugMouseScreen is null) { SetTarget(null); return; }
        WorldFocusTarget? best = null;
        var bestScore = float.PositiveInfinity;
        foreach (var candidate in objects.FocusTargets)
        {
            float score;
            var accepted = _directional
                ? WorldFocusCone.TryScore(camera.WorldCenter, _direction,
                    WorldObjectInteractionReach.ClosestPoint(candidate.Model!, camera.WorldCenter), Range, HalfAngleDegrees, out score)
                : DebugMouseWorld is { } debugMouse
                    ? TryWorldMouseScore(candidate, debugMouse, out score)
                    : WorldModelFocusPicker.TryScore(candidate.Model!, camera, DebugMouseScreen ?? mouseScreen, width, height, out score);
            if (accepted && IsBetter(candidate, score, best, bestScore)) { best = candidate; bestScore = score; }
        }
        var pointer = DebugMouseWorld ?? mouseWorld;
        foreach (var candidate in stairs.FocusTargets(_directional ? camera.WorldCenter : pointer, surfaceLevel,
                     _directional ? Range : 1))
        {
            var score = float.PositiveInfinity;
            var accepted = _directional
                ? WorldFocusCone.TryScore(camera.WorldCenter, _direction, candidate.Position, Range, HalfAngleDegrees, out score)
                : best is null && MathF.Floor(pointer.X) == MathF.Floor(candidate.Position.X) &&
                    MathF.Floor(pointer.Y) == MathF.Floor(candidate.Position.Y);
            if (!_directional) score = 0;
            if (accepted && IsBetter(candidate, score, best, bestScore)) { best = candidate; bestScore = score; }
        }
        SetTarget(best);
    }

    public Vector2? AssistStairs(Vector2 origin, Vector2 intent)
    {
        if (!_directional || Target is not { Stairs: not null } target ||
            !WorldFocusCone.TryScore(origin, intent, target.Position, 1.75f, 30, out _)) return null;
        var offset = target.Position - origin;
        return offset.LengthSquared() > 0.0001f ? Vector2.Normalize(offset) : null;
    }

    public Vector2? AssistManualMovement(Vector2 origin, Vector2 intent)
    {
        if (AssistStairs(origin, intent) is { } stairsDirection) return stairsDirection;
        if (!_directional || Target is not { Model: { } model } ||
            !WorldFocusCone.TryScore(origin, intent, WorldObjectInteractionReach.ClosestPoint(model, origin),
                Range, HalfAngleDegrees, out _)) return null;
        // Approach the selected object under normal collision. Do not plan a route beyond it.
        return Vector2.Normalize(intent);
    }

    public bool TryInteract(Vector2 origin, byte surfaceLevel) => Target is { } target &&
        (target.Stairs is not null ? stairs.TryActivate(target, origin, surfaceLevel) : objects.TryToggleFocused(target.Id));

    public void Clear()
    {
        SetTarget(null); _direction = Vector2.Zero; _lastIntent = Vector2.Zero; _directional = false;
        DebugDirection = null; DebugMouseWorld = null; DebugMouseScreen = null; _lastMouse = null;
    }

    private void SetTarget(WorldFocusTarget? target)
    {
        if (Target?.Model is { } previous) previous.IsFocused = false;
        var changed = Target?.Id != target?.Id || Target?.Kind != target?.Kind;
        Target = target;
        if (target?.Model is { } model) model.IsFocused = true;
        if (changed) EngineLog.WriteLine($"World {Status}");
    }

    private static bool IsBetter(WorldFocusTarget candidate, float score, WorldFocusTarget? best, float bestScore) =>
        score < bestScore || score == bestScore && (best is null || candidate.Id < best.Value.Id);

    private static bool TryWorldMouseScore(WorldFocusTarget target, Vector2 pointer, out float score)
    {
        score = Vector2.DistanceSquared(target.Position, pointer);
        return score <= 0.25f;
    }
}
