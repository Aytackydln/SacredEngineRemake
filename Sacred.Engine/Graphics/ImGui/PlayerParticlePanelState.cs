using System.Collections.Generic;
using System.Numerics;
using Sacred.Particles;

namespace Sacred.Engine.Graphics.ImGui;

internal sealed record PlayerParticlePanelState(
    IReadOnlyList<SacredParticleDefinition> Catalogue,
    IReadOnlySet<(uint TypeId, int? EventPreset, bool SelfTarget)> Enabled,
    IReadOnlyList<PlayerModelParticleState> ModelEffects,
    Vector2 PlayerPosition,
    int ActiveCount);

internal readonly record struct PlayerModelParticleState(string TextureName, ParticleTextureMode Mode, bool Enabled);

internal enum PlayerParticleAction { Play, Enable, Disable, StopAll, EnableModel, DisableModel }

internal sealed record PlayerParticleRequest(PlayerParticleAction Action, uint TypeId = 0,
    bool SelfTarget = true, Vector2 Point = default, float Height = 0, float Duration = 0.5f,
    string? TextureName = null, ParticleTextureMode Mode = default, int? EventPreset = null, bool FollowSelf = false);
