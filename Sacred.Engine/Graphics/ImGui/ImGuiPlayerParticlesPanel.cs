using System;
using System.Numerics;
using Sacred.Particles;
using DearImGui = ImGuiNET.ImGui;

namespace Sacred.Engine.Graphics.ImGui;

/// <summary>Native event bursts and continuous FX playback controls.</summary>
internal sealed class ImGuiPlayerParticlesPanel
{
    private string _filter = string.Empty;
    private bool _selfTarget = true;
    private bool _followSelf;
    private Vector2 _point;
    private bool _pointInitialized;
    private float _height;
    private float _duration = 0.5f;

    public void Draw(DebugUiControlState controls)
    {
        var state = controls.PlayerParticles;
        var requestedOpen = controls.RequestedParticlePanelOpen;
        var requestedMode = controls.RequestedParticlePreviewMode;
        controls.RequestedParticlePreviewMode = null;
        if (requestedOpen is { } panelOpen)
        {
            DearImGui.SetNextItemOpen(panelOpen);
            controls.RequestedParticlePanelOpen = null;
        }
        if (state is null || !DearImGui.CollapsingHeader("Play / Enable / Disable particle effects")) return;
        if (!_pointInitialized)
        {
            _point = state.PlayerPosition;
            _pointInitialized = true;
        }
        if (DearImGui.RadioButton("Self targeting", _selfTarget)) SetTarget(true);
        DearImGui.SameLine();
        if (DearImGui.RadioButton("Point targeting", !_selfTarget)) SetTarget(false);
        DearImGui.BeginDisabled(!_selfTarget);
        if (DearImGui.Checkbox("Follow player position (preview)", ref _followSelf))
            EngineLog.WriteLine($"Debug input: particle follow player {_followSelf}");
        DearImGui.EndDisabled();
        DearImGui.BeginDisabled(_selfTarget);
        DearImGui.SetNextItemWidth(190);
        if (DearImGui.InputFloat2("World point (tiles)", ref _point))
            EngineLog.WriteLine($"Debug input: particle point {_point}");
        if (DearImGui.SmallButton("Use player position"))
        {
            _point = state.PlayerPosition;
            EngineLog.WriteLine($"Debug input: particle point {_point}");
        }
        DearImGui.EndDisabled();
        DearImGui.SetNextItemWidth(190);
        if (DearImGui.InputFloat("Height (native units)", ref _height))
            EngineLog.WriteLine($"Debug input: particle height {_height}");
        DearImGui.SetNextItemWidth(190);
        var durationChanged = DearImGui.InputFloat("Emission (seconds)", ref _duration);
        _duration = float.IsFinite(_duration) ? Math.Clamp(_duration, 0.01f, 10) : 0.5f;
        if (durationChanged) EngineLog.WriteLine($"Debug input: particle preview duration {_duration}");
        DearImGui.SetNextItemWidth(190);
        DearImGui.InputText("Filter effects", ref _filter, 128);
        DearImGui.TextDisabled($"{state.Catalogue.Count} FX entries; {state.ActiveCount} active previews");
        DearImGui.TextWrapped("Play emits once or for the preview duration, then lets particles expire. Enable keeps emitting. Self samples the player's spawn position and mesh. Following is optional; point stays fixed.");
        if (DearImGui.Button("Stop all previews"))
            controls.ParticleRequests.Enqueue(new(PlayerParticleAction.StopAll));

        // Native event variants have authored lifetimes; continuous FX also allow timed previews.
        if (requestedMode is { } playMode) DearImGui.SetNextItemOpen(playMode);
        if (DearImGui.CollapsingHeader("One-time (Play)"))
            DrawCatalogue(controls, state, true);
        if (requestedMode is { } toggleMode) DearImGui.SetNextItemOpen(!toggleMode);
        if (DearImGui.CollapsingHeader("Other (Enable / Disable)"))
        {
            DearImGui.TextWrapped("Model-dependent effects on current equipment (self only)");
            if (state.ModelEffects.Count == 0)
                DearImGui.TextWrapped("Equip an item with authored effect anchors to show its model effects.");
            foreach (var effect in state.ModelEffects)
            {
                if (!Matches(effect.TextureName)) continue;
                var enabled = effect.Enabled;
                DearImGui.BeginDisabled(!_selfTarget);
                if (DearImGui.Checkbox($"{effect.TextureName} ({effect.Mode})##model", ref enabled))
                    controls.ParticleRequests.Enqueue(new(enabled ? PlayerParticleAction.EnableModel :
                        PlayerParticleAction.DisableModel, TextureName: effect.TextureName, Mode: effect.Mode));
                DearImGui.EndDisabled();
            }
            DearImGui.Separator();
            DrawCatalogue(controls, state, false);
        }
    }

    private void DrawCatalogue(DebugUiControlState controls, PlayerParticlePanelState state, bool once)
    {
        DearImGui.BeginChild(once ? "particle-play-catalogue" : "particle-toggle-catalogue", new Vector2(0, 200));
        foreach (var effect in state.Catalogue)
        {
            if (!Matches(effect.TypeName) && !Matches(effect.DisplayName ?? string.Empty) || (once != effect.OneTime)) continue;
            int? eventPreset = effect.IsEventPreset ? effect.Preset : null;
            DearImGui.PushID($"{effect.TypeId}:{eventPreset}");
            var supported = effect.Status == SacredParticleDefinitionStatus.Decoded && effect.Draw is not null;
            var enabled = state.Enabled.Contains((effect.TypeId, eventPreset, _selfTarget));
            DearImGui.BeginDisabled(!supported || effect.ModelBurstCount > 0 && !_selfTarget);
            if (DearImGui.SmallButton(once ? "Play" : enabled ? "Disable" : "Enable"))
                controls.ParticleRequests.Enqueue(new(once ? PlayerParticleAction.Play : enabled ?
                    PlayerParticleAction.Disable : PlayerParticleAction.Enable,
                    effect.TypeId, _selfTarget, _point, _height, _duration, EventPreset: eventPreset, FollowSelf: _followSelf));
            if (!once)
            {
                DearImGui.SameLine();
                if (DearImGui.SmallButton("Play"))
                    controls.ParticleRequests.Enqueue(new(PlayerParticleAction.Play, effect.TypeId,
                        _selfTarget, _point, _height, _duration, EventPreset: eventPreset, FollowSelf: _followSelf));
            }
            DearImGui.EndDisabled();
            DearImGui.SameLine();
            DearImGui.TextUnformatted((effect.DisplayName ?? effect.TypeName.Replace("TYPE_FX_", string.Empty)) + (effect.ModelBurstCount > 0 ? " (model)" : "") + (eventPreset is { } preset ? $" / event {preset}" : ""));
            if (DearImGui.IsItemHovered())
                DearImGui.SetTooltip($"{effect.TypeName} (0x{effect.TypeId:X})\n{effect.NativeClass ?? "Family not decoded"}\n{effect.Draw?.TextureName}\n{effect.Diagnostic}");
            if (!supported)
            {
                DearImGui.SameLine();
                DearImGui.TextDisabled("(not decoded)");
            }
            DearImGui.PopID();
        }
        DearImGui.EndChild();
    }

    private bool Matches(string name) => name.Contains(_filter, StringComparison.OrdinalIgnoreCase);

    private void SetTarget(bool self)
    {
        _selfTarget = self;
        EngineLog.WriteLine($"Debug input: particle targeting {(self ? "self" : "point")}");
    }
}
