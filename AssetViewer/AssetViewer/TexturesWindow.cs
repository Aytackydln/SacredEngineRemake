using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace AssetViewer.AssetViewer;

internal sealed class TexturesWindow : Window, IAssetViewerWindow
{
    private readonly AssetViewerSession _session;
    private readonly AssetTableControl<TextureAssetRow> _table;
    private readonly Image _image = new() { Stretch = Stretch.Fill };
    private readonly TextBlock _status = new() { Text = "Select a texture.", TextWrapping = TextWrapping.Wrap };
    private readonly ComboBox _channel = new() { ItemsSource = new[] { "RGBA", "RGB", "Alpha" }, SelectedIndex = 0 };
    private readonly Slider _zoom = new() { Minimum = 0.1, Maximum = 8, Value = 1, Width = 160 };
    private byte[]? _pixels;
    private int _width, _height;
    private WriteableBitmap? _bitmap;
    private CancellationTokenSource? _load;
    private bool _closed;
    private bool _fit;

    public TexturesWindow(AssetViewerSession session)
    {
        _session = session;
        Title = "Sacred Asset Viewer · Textures";
        Width = 1400; Height = 850;
        _table = new(session.Textures.Entries.Select(entry => new TextureAssetRow(entry)), row => $"{row.Archive} {row.EntryId} {row.Name} {row.Format}");
        var controls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        controls.Children.Add(_channel);
        controls.Children.Add(new TextBlock { Text = "Zoom", VerticalAlignment = VerticalAlignment.Center });
        controls.Children.Add(_zoom);
        var fit = new CheckBox { Content = "Fit", IsChecked = false };
        controls.Children.Add(fit);
        var background = new ComboBox { ItemsSource = new[] { "Dark background", "Light background", "Gray background" }, SelectedIndex = 0 };
        controls.Children.Add(background);
        var preview = new Border { Background = Brushes.Black, Child = _image };
        background.SelectionChanged += (_, _) => preview.Background = background.SelectedIndex switch
        { 1 => Brushes.White, 2 => Brushes.Gray, _ => Brushes.Black };
        fit.IsCheckedChanged += (_, _) => { _fit = fit.IsChecked == true; _image.Stretch = _fit ? Stretch.Uniform : Stretch.Fill; UpdateZoom(_fit); };
        _zoom.ValueChanged += (_, _) => UpdateZoom(fit.IsChecked == true);
        _channel.SelectionChanged += (_, _) => UpdateBitmap();
        var right = new Grid { RowDefinitions = new("Auto,*,Auto") };
        right.Children.Add(controls);
        var scroll = new ScrollViewer { Content = preview, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto };
        fit.IsCheckedChanged += (_, _) =>
        {
            scroll.HorizontalScrollBarVisibility = _fit ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
            scroll.VerticalScrollBarVisibility = _fit ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
        };
        Grid.SetRow(scroll, 1); right.Children.Add(scroll);
        Grid.SetRow(_status, 2); right.Children.Add(_status);
        var root = new Grid { ColumnDefinitions = new("3*,5,2*"), Margin = new Thickness(12) };
        root.Children.Add(_table);
        var splitter = new GridSplitter { ResizeDirection = GridResizeDirection.Columns };
        Grid.SetColumn(splitter, 1); root.Children.Add(splitter);
        Grid.SetColumn(right, 2); root.Children.Add(right);
        Content = root;
        _table.SelectedChanged += row => Ready = LoadAsync(row);
        Closed += (_, _) => { _closed = true; _load?.Cancel(); _image.Source = null; _bitmap?.Dispose(); };
    }

    public Task Ready { get; private set; } = Task.CompletedTask;
    public void Select(string archive, uint id) => _table.Select(row => row.Archive.Equals(archive, StringComparison.OrdinalIgnoreCase) && row.EntryId == id);
    public void SaveScreenshot(string path)
    {
        (_bitmap ?? throw new InvalidOperationException("Select a texture first.")).Save(path);
        Console.WriteLine($"[Assets] Screenshot saved: {path}");
    }

    private async Task LoadAsync(TextureAssetRow row)
    {
        _load?.Cancel(); _load?.Dispose(); _load = new CancellationTokenSource();
        var token = _load.Token;
        _pixels = null; _image.Source = null; _bitmap?.Dispose(); _bitmap = null;
        _status.Text = $"Loading {row.Archive}:{row.EntryId} {row.Name}...";
        try
        {
            var asset = await Task.Run(() => _session.Textures.LoadTextureAsync(row.Entry, token), token);
            token.ThrowIfCancellationRequested();
            if (_closed) return;
            _pixels = asset.Rgba8; _width = asset.Width; _height = asset.Height;
            UpdateBitmap();
            _status.Text = $"{row.Archive}:{row.EntryId} · {row.Name}\n{_width} × {_height} · {row.Format} · offset 0x{row.Offset:X} · {row.Size:N0} bytes";
            Console.WriteLine($"[Assets] Texture ready: {row.Archive}:{row.EntryId} {row.Name}, {_width}x{_height}.");
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            if (!token.IsCancellationRequested && !_closed) _status.Text = error.Message;
            Console.WriteLine($"[Assets] Texture failed: {error.Message}");
        }
    }

    private void UpdateBitmap()
    {
        if (_pixels is null) return;
        var pixels = (byte[])_pixels.Clone();
        for (var i = 0; i < pixels.Length; i += 4)
        {
            if (_channel.SelectedIndex == 1) pixels[i + 3] = 255;
            if (_channel.SelectedIndex == 2)
            { pixels[i] = pixels[i + 1] = pixels[i + 2] = pixels[i + 3]; pixels[i + 3] = 255; }
        }
        var bitmap = new WriteableBitmap(new PixelSize(_width, _height), new Vector(96, 96), PixelFormat.Rgba8888, AlphaFormat.Unpremul);
        using (var framebuffer = bitmap.Lock())
            for (var row = 0; row < _height; row++)
                Marshal.Copy(pixels, row * _width * 4, framebuffer.Address + row * framebuffer.RowBytes, _width * 4);
        _image.Source = bitmap;
        _bitmap?.Dispose(); _bitmap = bitmap;
        UpdateZoom(_fit);
    }

    private void UpdateZoom(bool fit)
    {
        _image.Width = fit ? double.NaN : _width * _zoom.Value;
        _image.Height = fit ? double.NaN : _height * _zoom.Value;
    }
}
