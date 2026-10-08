using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using AssetViewer.ItemViewer;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Sacred.Assets.Paks.Texture;
using Sacred.Granny.Assets;

namespace AssetViewer.AssetViewer;

internal sealed class ModelPreviewPane : UserControl
{
    private readonly AssetViewerSession _session;
    private readonly ModelViewerControl _viewer = new();
    private readonly Action _resetRotation;
    private CancellationTokenSource? _load;
    private bool _closed;

    public ModelPreviewPane(AssetViewerSession session)
    {
        _session = session;
        _viewer.SetAssetPreview(true);
        var yaw = new Slider { Minimum = -Math.PI, Maximum = Math.PI, Width = 180 };
        var pitch = new Slider { Minimum = -Math.PI, Maximum = Math.PI, Width = 140 };
        var reset = new Button { Content = "Reset camera" };
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 0, 0, 8) };
        bar.Children.Add(new TextBlock { Text = "Rotate", VerticalAlignment = VerticalAlignment.Center });
        bar.Children.Add(yaw);
        bar.Children.Add(pitch);
        bar.Children.Add(reset);
        yaw.ValueChanged += (_, _) => _viewer.SetUserRotation((float)yaw.Value, (float)pitch.Value, 0);
        pitch.ValueChanged += (_, _) => _viewer.SetUserRotation((float)yaw.Value, (float)pitch.Value, 0);
        _viewer.HorizontalRotationChanged += value => yaw.Value = value;
        _resetRotation = () =>
        {
            yaw.Value = 0;
            pitch.Value = 0;
            _viewer.SetUserRotation(0, 0, 0);
        };
        reset.Click += (_, _) => _resetRotation();
        var root = new DockPanel();
        DockPanel.SetDock(bar, Dock.Top);
        root.Children.Add(bar);
        root.Children.Add(_viewer);
        Content = root;
    }

    public Task CurrentLoad { get; private set; } = Task.CompletedTask;

    public void ShowStatus(string status)
    {
        _load?.Cancel();
        _resetRotation();
        _viewer.ShowStatus(status);
        CurrentLoad = Task.CompletedTask;
    }

    public Task LoadAsync(string name, Func<CancellationToken, Task<GrnAsset>> loader,
        IReadOnlyList<ModelPreviewVisual> visuals, bool compositeSlices = false)
    {
        _resetRotation();
        _load?.Cancel();
        _load?.Dispose();
        _load = new CancellationTokenSource();
        CurrentLoad = LoadCoreAsync(name, loader, visuals, compositeSlices, _load.Token);
        return CurrentLoad;
    }

    public void Cancel()
    {
        _closed = true;
        _load?.Cancel();
    }

    public void SaveScreenshot(string path) => _viewer.SaveScreenshot(path);
    public void RotateHorizontally(float radians) => _viewer.RotateHorizontally(radians);
    public void SetAnimationPlaying(bool playing) => _viewer.SetAnimationPlaying(playing);
    public void SetAnimationTime(float seconds) => _viewer.SetAnimationTime(seconds);

    private async Task LoadCoreAsync(string name, Func<CancellationToken, Task<GrnAsset>> loader,
        IReadOnlyList<ModelPreviewVisual> visuals, bool compositeSlices, CancellationToken token)
    {
        _viewer.ShowStatus($"{name}: loading...");
        try
        {
            var asset = await Task.Run(() => loader(token), token);
            token.ThrowIfCancellationRequested();
            if (_closed) return;
            var effects = await Task.Run(() => AssetPreviewEffects.Create(asset, visuals, compositeSlices), token);
            token.ThrowIfCancellationRequested();
            _viewer.ShowModel(asset, Vector3.Zero, 1, 1, effects);
            var loaded = new Dictionary<string, ModelTextureBinding>(StringComparer.OrdinalIgnoreCase);
            var failures = 0;
            var aliases = new Dictionary<string, ModelTextureReference>(StringComparer.OrdinalIgnoreCase);
            // Resolve variants against each source mesh before assembling a character's combined materials.
            foreach (var preview in visuals)
            {
                var visual = preview.Item;
                token.ThrowIfCancellationRequested();
                var source = visuals.Count == 1 ? asset : await _session.Models.LoadModelAsync(visual.ModelName, cancellationToken: token);
                var names = source.Mesh?.Surfaces.Select(surface => surface.TextureName)
                    .Where(surface => !string.IsNullOrWhiteSpace(surface)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray() ?? [];
                var desc = visual.ModelDesc;
                var hasEffect = _session.Textures.TryGetTextureName(desc.EffectTextureId, out var effectName) &&
                    names.Any(surface => _session.Textures.TryResolveTextureName(surface!, out var resolved) &&
                        resolved.Equals(effectName, StringComparison.OrdinalIgnoreCase));
                foreach (var surface in names)
                    aliases[surface!] = ModelTextureResolver.Resolve(_session.Textures, desc.TextureId, desc.EffectTextureId,
                        desc.GraphicFlags, hasEffect, names.Length == 1, surface);
            }
            foreach (var surface in asset.Mesh?.Surfaces ?? [])
            {
                var key = surface.TextureName;
                if (string.IsNullOrWhiteSpace(key) || loaded.ContainsKey(key)) continue;
                var reference = aliases.GetValueOrDefault(key, ModelTextureReference.Static(key));
                try
                {
                    var texture = await _session.Textures.LoadTextureAsync(reference.TextureName, token);
                    TextureAsset? overlay = null;
                    if (reference.HasOverlay)
                        overlay = await _session.Textures.LoadTextureAsync(reference.OverlayTextureName!, token);
                    loaded[key] = new ModelTextureBinding(texture with { Animation = reference.Animation },
                        overlay is null ? null : overlay with { Animation = reference.OverlayAnimation }, reference.OverlayMode);
                }
                catch (Exception error) when (error is IOException or NotSupportedException)
                {
                    failures++;
                    Console.WriteLine($"[Assets] Texture {key}: {error.Message}");
                }
            }
            // Effect textures use their native names, independently of the item's surface aliases.
            foreach (var key in effects.TextureNames)
            {
                token.ThrowIfCancellationRequested();
                if (loaded.ContainsKey(key)) continue;
                try
                {
                    loaded[key] = new ModelTextureBinding(await _session.Textures.LoadTextureAsync(key, token));
                }
                catch (Exception error) when (error is IOException or NotSupportedException)
                {
                    failures++;
                    Console.WriteLine($"[Assets] Effect texture {key}: {error.Message}");
                }
            }
            token.ThrowIfCancellationRequested();
            await _viewer.ShowTexturesAsync(loaded, failures, token);
            Console.WriteLine($"[Assets] Preview ready: {name}; {asset.Mesh?.Vertices.Length ?? 0} vertices, {loaded.Count} textures, {effects.Surfaces.Count} effect surfaces, {failures} missing.");
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            if (!token.IsCancellationRequested && !_closed) _viewer.ShowStatus($"{name}: {error.Message}");
            Console.WriteLine($"[Assets] Preview failed: {name}: {error}");
        }
    }
}
