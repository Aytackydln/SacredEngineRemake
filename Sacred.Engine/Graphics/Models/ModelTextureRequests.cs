using System;
using System.Collections.Generic;
using Sacred.Assets.Paks.Texture;
using Sacred.Engine.Scene;

namespace Sacred.Engine.Graphics.Models;

/// <summary>Resolves scene materials and reserves capacity for equipment effects before ordinary materials.</summary>
internal sealed class ModelTextureRequests
{
    private readonly HashSet<string> _activeNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _baseNames = [];
    private readonly List<string> _overlayNames = [];
    private readonly List<string> _effectNames = [];

    public IReadOnlyList<string> BaseNames => _baseNames;
    public IReadOnlyList<string> OverlayNames => _overlayNames;
    public IReadOnlyList<string> EffectNames => _effectNames;

    public bool Contains(string name) => _activeNames.Contains(name);

    public void LimitTo(int maximumTextureCount)
    {
        _activeNames.Clear();
        // Joined scenes can contain more materials than the descriptor heap holds.
        // Select the working set before uploads, including effects discovered after
        // ordinary model textures filled the cache. Reconciliation can then evict
        // lower-priority materials instead of leaving the effect queued forever.
        Retain(_effectNames);
        Retain(_baseNames);
        Retain(_overlayNames);
        _baseNames.RemoveAll(name => !_activeNames.Contains(name));
        _overlayNames.RemoveAll(name => !_activeNames.Contains(name));
        _effectNames.RemoveAll(name => !_activeNames.Contains(name));
        return;

        void Retain(List<string> names)
        {
            foreach (var name in names)
            {
                if (_activeNames.Count >= maximumTextureCount) break;
                _activeNames.Add(name);
            }
        }
    }

    public void Collect(IReadOnlyList<SceneModel> models)
    {
        _activeNames.Clear();
        _baseNames.Clear();
        _overlayNames.Clear();
        _effectNames.Clear();

        foreach (var model in models)
        {
            foreach (var surface in model.Geometry.BindMesh.Surfaces)
            {
                var reference = model.ResolveTextureReference(surface.TextureName);
                if (!string.IsNullOrWhiteSpace(reference.TextureName))
                {
                    _activeNames.Add(reference.TextureName);
                    _baseNames.Add(reference.TextureName);
                }

                if (!string.IsNullOrWhiteSpace(reference.OverlayTextureName))
                    _activeNames.Add(reference.OverlayTextureName);
                if (reference.HasOverlay)
                    _overlayNames.Add(reference.OverlayTextureName!);
                if (reference.Animation.HasSecondaryGlow || reference.OverlayAnimation.HasSecondaryGlow)
                {
                    _activeNames.Add(TextureAnimation.MultitextureGlowName);
                    _overlayNames.Add(TextureAnimation.MultitextureGlowName);
                }
            }

            if (model.EquipmentEffects is not { } effects)
                continue;

            foreach (var name in effects.TextureNames)
            {
                _activeNames.Add(name);
                _effectNames.Add(name);
            }
        }
    }
}
