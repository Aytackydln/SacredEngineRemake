using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Sacred.Core.Pak.Items;
using Sacred.Core.Pak.Weapon;
using Sacred.Granny.Abstractions;

namespace AssetViewer.AssetViewer;

internal sealed class ModelsWindow : Window, IAssetViewerWindow
{
    private readonly AssetTableControl<ModelAssetRow> _table;
    private readonly ModelPreviewPane _preview;
    private readonly TextBox _details = new() { IsReadOnly = true, AcceptsReturn = true, FontFamily = new("Consolas") };

    public ModelsWindow(AssetViewerSession session)
    {
        Title = "Sacred Asset Viewer · Models (Items.pak)";
        Width = 1400; Height = 850;
        _table = new(session.Items.Select(item => new ModelAssetRow(item)), row => $"{row.EntryId} {row.ResourceId} {row.Model} {row.Category}");
        _preview = new(session);
        var root = new Grid { ColumnDefinitions = new("3*,5,2*"), Margin = new Thickness(12) };
        root.Children.Add(_table);
        var splitter = new GridSplitter { ResizeDirection = GridResizeDirection.Columns };
        Grid.SetColumn(splitter, 1); root.Children.Add(splitter);
        var right = new Grid { RowDefinitions = new("3*,2*") };
        right.Children.Add(_preview);
        Grid.SetRow(_details, 1); right.Children.Add(_details);
        Grid.SetColumn(right, 2); root.Children.Add(right);
        Content = root;
        _table.SelectedChanged += row =>
        {
            _details.Text = Describe(row.Item);
            if (string.IsNullOrWhiteSpace(row.Model)) { _preview.ShowStatus($"Items.pak row {row.EntryId} has no 3D model."); return; }
            var equipment = session.Equipment.Where(item => item.IdemId == row.EntryId)
                .Select(item => (SacredEquipment?)item).FirstOrDefault();
            _ = _preview.LoadAsync(row.Model, token => session.Models.LoadModelAsync(row.Model,
                GrnMeshExtractionMode.CompositeSlices, token), [new(row.Item, equipment)], compositeSlices: true);
        };
        Closed += (_, _) => _preview.Cancel();
    }

    public Task Ready => _preview.CurrentLoad;
    public void Select(ushort id) => _table.Select(row => row.EntryId == id);
    public void SaveScreenshot(string path) => _preview.SaveScreenshot(path);
    public void RotateHorizontally(float radians) => _preview.RotateHorizontally(radians);

    private static string Describe(ItemsPakEntry item)
    {
        var text = new StringBuilder();
        text.AppendLine($"Items.pak row {item.ItemIndex}, descriptor at 0x{item.EntryInfo.ModelDescOffset:X}");
        foreach (var field in typeof(ItemsPakEntryModelDescLayout).GetFields())
        {
            if (field.IsStatic) continue;
            var offset = Marshal.OffsetOf<ItemsPakEntryModelDescLayout>(field.Name);
            text.AppendLine($"0x{offset.ToInt32():X2} {field.Name}: {field.GetValue(item.ModelDesc)}");
        }
        text.AppendLine("\nRaw descriptor (128 bytes):");
        for (var offset = 0; offset < ItemsPakEntryModelDescLayout.SerializedSize; offset += 16)
        {
            text.Append($"{offset:X2}: ");
            for (var i = 0; i < 16; i++) text.Append($"{item.ModelDesc.GetRawByte(offset + i):X2} ");
            text.AppendLine();
        }
        return text.ToString();
    }
}
