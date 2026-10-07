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
        PreviewReady = LoadModel(SacredItemDataModel.FromSacredEquipment(equipment, _session.Data.GameResStore),
            ItemPreviewRotationMode.RawXyz, ItemPreviewPivotMode.ModelOrigin);
    }

    internal void SavePreviewScreenshot(string path) => _modelViewer.SaveScreenshot(path);
}
