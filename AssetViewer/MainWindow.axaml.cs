using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AssetViewer.AssetViewer;
using AssetViewer.ItemViewer;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace AssetViewer;

public partial class MainWindow : Window
{
    private readonly SacredItemFilterSaveStore _settings = SacredItemFilterSaveStore.CreateDefault();
    private readonly Dictionary<string, Window> _windows = new(StringComparer.OrdinalIgnoreCase);
    private AssetViewerSession? _session;
    private bool _loading;
    private bool _closed;
    internal IAssetViewerWindow? ActiveViewer { get; private set; }
    internal Task Loading { get; private set; } = Task.CompletedTask;

    public MainWindow()
    {
        InitializeComponent();
        var args = Environment.GetCommandLineArgs();
        var index = Array.IndexOf(args, "--game-directory");
        GameDirectoryBox.Text = index >= 0 && index + 1 < args.Length ? args[index + 1] : _settings.Load().GameDirectory;
        Opened += (_, _) =>
        {
            Loading = LoadGameAsync();
            AssetViewerConsole.Start(this);
        };
        Closed += (_, _) =>
        {
            _closed = true;
            CloseViewers();
            _session?.Dispose();
            _session = null;
        };
    }

    private async void Browse_OnClick(object? sender, RoutedEventArgs e)
    {
        var selection = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        { Title = "Choose Sacred game installation", AllowMultiple = false });
        if (selection.FirstOrDefault()?.TryGetLocalPath() is { } path)
        {
            GameDirectoryBox.Text = path;
            Loading = LoadGameAsync();
            await Loading;
        }
    }

    private async void Load_OnClick(object? sender, RoutedEventArgs e)
    {
        Loading = LoadGameAsync();
        await Loading;
    }

    internal async Task LoadGameAsync(string? directory = null)
    {
        if (_loading || _closed) return;
        if (directory is not null) GameDirectoryBox.Text = directory;
        _loading = true;
        LoadButton.IsEnabled = false; ViewerButtons.IsEnabled = false;
        StatusText.Text = "Loading game assets…";
        try
        {
            var session = await AssetViewerSession.LoadAsync(GameDirectoryBox.Text ?? string.Empty);
            if (_closed) { session.Dispose(); return; }
            CloseViewers();
            _session?.Dispose();
            _session = session;
            GameDirectoryBox.Text = session.GameDirectory;
            var settings = _settings.Load();
            _settings.Save(settings with { GameDirectory = session.GameDirectory });
            StatusText.Text = $"Loaded {session.Items.Length:N0} item records, {session.Equipment.Length:N0} equipment, {session.Textures.Entries.Count:N0} textures.";
        }
        catch (Exception error)
        {
            StatusText.Text = $"Could not load game: {error.Message}\nChoose the installation folder containing pak and scripts.";
            Console.WriteLine($"[Assets] Loading failed: {error}");
        }
        finally
        {
            _loading = false;
            LoadButton.IsEnabled = true;
            ViewerButtons.IsEnabled = _session is not null;
        }
    }

    internal Window OpenViewer(string kind)
    {
        if (_session is null || _loading) throw new InvalidOperationException("Load a game location first.");
        if (!_windows.TryGetValue(kind, out var window))
        {
            window = kind.ToLowerInvariant() switch
            {
                "models" => new ModelsWindow(_session),
                "effects" => new EffectsWindow(_session),
                "equipment" => new EquipmentWindow(_session),
                "textures" => new TexturesWindow(_session),
                "characters" => new CharactersWindow(_session),
                _ => throw new ArgumentException("Viewer must be models, effects, equipment, textures or characters.")
            };
            _windows[kind] = window;
            window.Closed += (_, _) => { _windows.Remove(kind); if (ReferenceEquals(ActiveViewer, window)) ActiveViewer = null; };
            window.Activated += (_, _) => ActiveViewer = (IAssetViewerWindow)window;
            window.Show(this);
        }
        ActiveViewer = (IAssetViewerWindow)window;
        Console.WriteLine($"[Assets] View switched: {kind}.");
        return window;
    }

    internal async Task<Window> OpenViewerAsync(string kind)
    {
        var window = OpenViewer(kind);
        if (!window.IsLoaded)
        {
            var loaded = new TaskCompletionSource();
            window.Loaded += OnLoaded;
            void OnLoaded(object? sender, RoutedEventArgs args)
            {
                window.Loaded -= OnLoaded;
                loaded.TrySetResult();
            }
            await loaded.Task;
        }
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        return window;
    }

    private void CloseViewers()
    {
        foreach (var window in _windows.Values.ToArray()) window.Close();
        _windows.Clear(); ActiveViewer = null;
    }

    internal void SaveWindowScreenshot(string path)
    {
        var window = _windows.Values.FirstOrDefault(candidate => ReferenceEquals(candidate, ActiveViewer)) ?? this;
        var scale = window.RenderScaling;
        var size = new PixelSize(Math.Max(1, (int)Math.Ceiling(window.Bounds.Width * scale)), Math.Max(1, (int)Math.Ceiling(window.Bounds.Height * scale)));
        using var bitmap = new RenderTargetBitmap(size, new Vector(96 * scale, 96 * scale));
        bitmap.Render(window);
        bitmap.Save(path);
        Console.WriteLine($"[Assets] UI screenshot saved: {path}");
    }

    private void Models_OnClick(object? sender, RoutedEventArgs e) => OpenViewer("models");
    private void Effects_OnClick(object? sender, RoutedEventArgs e) => OpenViewer("effects");
    private void Equipment_OnClick(object? sender, RoutedEventArgs e) => OpenViewer("equipment");
    private void Textures_OnClick(object? sender, RoutedEventArgs e) => OpenViewer("textures");
    private void Characters_OnClick(object? sender, RoutedEventArgs e) => OpenViewer("characters");
}
