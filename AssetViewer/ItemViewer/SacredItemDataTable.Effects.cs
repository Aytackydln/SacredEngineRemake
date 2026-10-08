using System;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;

namespace AssetViewer.ItemViewer;

public partial class SacredItemDataTable
{
    private bool _effectsEnabled = true;
    private SacredItemDataModel? _previewItem;

    private void EffectsCheckBox_OnCheckedChanged(object? sender, RoutedEventArgs e)
    {
        var enabled = sender is ToggleButton { IsChecked: true };
        if (enabled == _effectsEnabled) return;
        _effectsEnabled = enabled;
        Console.WriteLine($"[Inventory] Effects: {(_effectsEnabled ? "enabled" : "disabled")}.");

        // Reload through the existing cancellation path. The rotation sliders
        // retain their values, and future selections use the same setting.
        if (_previewItem is { } item && !string.IsNullOrWhiteSpace(item.ModelName))
            PreviewReady = LoadModel(item);
    }

    internal void SetEquipmentEffectsEnabled(bool enabled) => EffectsCheckBox.IsChecked = enabled;
}
