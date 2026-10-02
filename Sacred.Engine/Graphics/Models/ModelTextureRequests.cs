using System;
using System.Collections.Generic;
using Sacred.Engine.Scene;

namespace Sacred.Engine.Graphics.Models;

/// <summary>Resolves scene materials once, preserving base, overlay, then equipment upload priority.</summary>
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
