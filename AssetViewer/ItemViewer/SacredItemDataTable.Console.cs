using System;
using System.Threading.Tasks;

namespace AssetViewer.ItemViewer;

public partial class SacredItemDataTable
{
    internal Task PreviewReady { get; private set; } = Task.CompletedTask;

    internal void SelectEquipment(uint id)
    {
        if (_session is null || !_hasLoaded) throw new InvalidOperationException("Equipment viewer is not loaded.");
        if (!_session.Data.GamePakStore.Weapons.TryGetValue(id, out var equipment))
            throw new ArgumentException($"Unknown equipment {id}.");
        ResetModelRotationSliders();
        _tableViewModel.SelectDescription(id);
        ShowNativeDescription(id);
        Console.WriteLine($"[Equipment] Description for {id}:\n{_tableViewModel.DescriptionText}");
        PreviewReady = Task.WhenAll(LoadModel(SacredItemDataModel.FromSacredEquipment(equipment, _session.Data.GameResStore)),
            _nativeDescription?.Ready ?? Task.CompletedTask);
    }

    private void ShowNativeDescription(uint? id)
    {
        if (_nativeDescription is null || _session is null) return;
        if (id is { } value && _session.Data.GamePakStore.Weapons.TryGetValue(value, out var equipment))
        {
            ItemDescriptionHost.Content = _nativeDescription;
            _nativeDescription.Show(Sacred.Inventory.Items.SacredItemInstance.FromDefinition(equipment));
        }
        else
        {
            _nativeDescription.Clear();
            ItemDescriptionHost.Content = new Avalonia.Controls.TextBlock
            { Text = "Select an item to view its description.", TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        }
    }

    internal void SavePreviewScreenshot(string path) => _modelViewer.SaveScreenshot(path);

    internal void RotateEquipment(float radians)
    {
        if (!float.IsFinite(radians)) throw new ArgumentOutOfRangeException(nameof(radians));
        var yaw = (float)Math.IEEERemainder(ModelYawSlider.Value + radians, Math.Tau);
        SetModelRotationSliders(new(yaw, (float)ModelPitchSlider.Value, (float)ModelRollSlider.Value));
        Console.WriteLine($"[Inventory] User rotation: yaw={ModelYawSlider.Value}, pitch={ModelPitchSlider.Value}, roll={ModelRollSlider.Value}.");
    }
    internal void SetEquipmentRotation(float yaw, float pitch, float roll)
    {
        if (!float.IsFinite(yaw) || !float.IsFinite(pitch) || !float.IsFinite(roll))
            throw new ArgumentOutOfRangeException(nameof(yaw));
        SetModelRotationSliders(new(yaw, pitch, roll));
        Console.WriteLine($"[Inventory] User rotation: yaw={ModelYawSlider.Value}, pitch={ModelPitchSlider.Value}, roll={ModelRollSlider.Value}.");
    }
}
