using System.Globalization;
using System.Runtime.InteropServices.JavaScript;
using AssetViewer.AssetViewer;
using AssetViewer.Browser.Files;
using AssetViewer.Browser.Rendering;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Sacred.Core.World.Portals;
using Sacred.Particles;

namespace AssetViewer.Browser.Startup;

internal sealed class BrowserRoot : UserControl
{
    internal static BrowserRoot? Current { get; private set; }
    private readonly ContentControl _viewer = new();
    private readonly TextBlock _status = new() { Text = "Choose your Sacred installation. Files stay on this device.", TextWrapping = TextWrapping.Wrap };
    private readonly Button _choose = new() { Content = "Open Sacred installation…" };
    private readonly StackPanel _navigation = new() { Orientation = Orientation.Horizontal, Spacing = 8, IsEnabled = false };
    private AssetViewerSession? _session;
    private BrowserInstallation? _installation;
    private BrowserViewerPage? _page;
    private string? _kind;
    private bool _loading;

    internal BrowserRoot()
    {
        Current = this;
        var header = new StackPanel { Spacing = 8, Margin = new Thickness(12) };
        header.Children.Add(_choose);
        header.Children.Add(_status);
        foreach (var kind in new[] { "models", "characters", "equipment", "effects", "textures", "drops" })
        {
            var button = new Button { Content = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(kind) };
            button.Click += (_, _) => OpenViewer(kind);
            _navigation.Children.Add(button);
        }
        header.Children.Add(_navigation);
        var layout = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        layout.Children.Add(header);
        layout.Children.Add(_viewer);
        Content = layout;
        _choose.Click += async (_, _) =>
        {
            if (_loading) return;
            try { await LoadAsync(await BrowserInterop.PickInstallationAsync()); }
            catch (Exception error) { Report(error); }
        };
    }

    internal async Task LoadAsync(string manifest)
    {
        if (_loading) throw new InvalidOperationException("An installation is already loading.");
        _loading = true;
        _choose.IsEnabled = _navigation.IsEnabled = false;
        var installation = new BrowserInstallation(manifest);
        try
        {
            await WebGpuInterop.EnsureAvailableAsync();
            var session = await installation.LoadAsync(message => { _status.Text = message; Console.WriteLine($"[Browser] {message}"); });
            _page?.Close(); _viewer.Content = null; _page = null; _kind = null;
            _session?.Dispose(); _installation?.Dispose();
            _session = session; _installation = installation;
            _status.Text = $"{session.Items.Length:N0} items · {session.Equipment.Length:N0} equipment · {session.Textures.Entries.Count:N0} textures · WebGPU";
            OpenViewer("models");
        }
        catch { installation.Dispose(); throw; }
        finally { _loading = false; _choose.IsEnabled = true; _navigation.IsEnabled = _session is not null; }
    }

    internal BrowserViewerPage OpenViewer(string kind)
    {
        if (_session is null) throw new InvalidOperationException("Choose an installation first.");
        if (_kind == kind && _page is not null) return _page;
        var page = kind switch
        {
            "models" => (BrowserViewerPage)new ModelsWindow(_session),
            "characters" => new CharactersWindow(_session),
            "equipment" => new EquipmentWindow(_session),
            "effects" => new EffectsWindow(_session),
            "textures" => new TexturesWindow(_session),
            "drops" => new ItemDropSimulatorWindow(_session),
            _ => throw new ArgumentException("Unknown viewer.", nameof(kind))
        };
        page.Width = page.Height = double.NaN;
        _page?.Close();
        _page = page; _kind = kind; _viewer.Content = page;
        Console.WriteLine($"[Browser] View switched: {kind}.");
        return page;
    }

    private void Report(Exception error)
    {
        _status.Text = error.Message;
        Console.WriteLine($"[Browser] {error}");
    }

    private async Task<BrowserViewerPage> OpenViewerAsync(string kind)
    {
        var page = OpenViewer(kind);
        if (!page.IsLoaded)
        {
            var loaded = new TaskCompletionSource();
            void OnLoaded(object? sender, RoutedEventArgs args)
            { page.Loaded -= OnLoaded; loaded.TrySetResult(); }
            page.Loaded += OnLoaded;
            await loaded.Task;
        }
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        if (page is IAssetViewerWindow viewer) await viewer.Ready;
        return page;
    }

    internal async Task<string> ExecuteAsync(string command)
    {
        var parts = command.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        var argument = parts.Length > 1 ? parts[1] : string.Empty;
        Console.WriteLine($"[Browser cheat] {command}");
        switch (parts[0])
        {
            case "animation-perf": return BrowserAnimationDiagnostics.Execute(argument);
            case "skinning-validate": return await WebGpuModelViewportHost.ValidateCurrentSkinningAsync();
            case "texture-cache":
                var cache = (_session ?? throw new InvalidOperationException("Choose an installation first.")).TextureCache.Statistics;
                return FormattableString.Invariant($"{{\"bytes\":{cache.Bytes},\"hits\":{cache.Hits},\"misses\":{cache.Misses},\"entries\":{cache.Entries}}}");
            case "capture-size":
                var size = argument.Split(' ', 2);
                var width = int.Parse(size[0]); var height = int.Parse(size[1]);
                if (width < 1 || height < 1 || width > 8192 || height > 8192) throw new ArgumentOutOfRangeException(nameof(argument));
                WebGpuModelViewportHost.CaptureSize = (width, height); break;
            case "fx-cases":
                var catalogue = SacredParticleCatalogue.LoadEmbedded();
                var cases = catalogue.Definitions.Concat(catalogue.EventDefinitions)
                    .Where(FxPreviewSupport.CanPreview)
                    .Select(definition => $"{definition.TypeId} {(definition.IsEventPreset ? definition.Preset?.ToString(CultureInfo.InvariantCulture) : "script")}");
                var portals = _session!.Items.Where(item => SacredPortalAppearance.TryGetVariant(item.ModelName, out _))
                    .Select(item => $"{item.ItemIndex} mesh");
                return string.Join('\n', cases.Concat(portals).Distinct());
            case "model": ((ModelsWindow)await OpenViewerAsync("models")).Select(ushort.Parse(argument)); break;
            case "character": ((CharactersWindow)await OpenViewerAsync("characters")).Select(ushort.Parse(argument)); break;
            case "inventory-slot":
                var inventory = (CharactersWindow)await OpenViewerAsync("characters");
                if (argument == "all") inventory.ClearInventorySlotFilter();
                else inventory.SelectInventorySlot(int.Parse(argument));
                break;
            case "inventory": ((CharactersWindow)await OpenViewerAsync("characters")).PrintInventory(); break;
            case "animation": ((CharactersWindow)await OpenViewerAsync("characters")).SelectAnimation(argument); break;
            case "animation-time": ((CharactersWindow)await OpenViewerAsync("characters")).SetAnimationTime(float.Parse(argument, CultureInfo.InvariantCulture)); break;
            case "animation-play": ((CharactersWindow)await OpenViewerAsync("characters")).SetAnimationPlaying(bool.Parse(argument)); break;
            case "item": ((EquipmentWindow)await OpenViewerAsync("equipment")).Select(uint.Parse(argument)); break;
            case "drop":
                var drop = argument.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (drop.Length is not (5 or 6)) throw new ArgumentException("Usage: drop <difficulty> <hero-level> <category> <class> <seed> [quality]");
                var choice = ItemDropCategoryChoice.Parse(drop[2]);
                await ((ItemDropSimulatorWindow)await OpenViewerAsync("drops")).RollAsync(new(ushort.Parse(drop[1]), uint.Parse(drop[0]),
                    uint.Parse(drop[4]), choice.Category, uint.Parse(drop[3]), choice.Mode,
                    drop.Length == 5 || drop[5] == "random" ? null : uint.Parse(drop[5]))); break;
            case "drop-replay": await ((ItemDropSimulatorWindow)await OpenViewerAsync("drops")).ReplayAsync(); break;
            case "drop-clear": ((ItemDropSimulatorWindow)await OpenViewerAsync("drops")).ClearDrops(); break;
            case "effects": ((EquipmentWindow)await OpenViewerAsync("equipment")).SetEffectsEnabled(bool.Parse(argument)); break;
            case "texture":
                var texture = argument.Split(' ', 2);
                ((TexturesWindow)await OpenViewerAsync("textures")).Select(texture[0], uint.Parse(texture[1])); break;
            case "equip":
                var equip = argument.Split(' ', 2);
                ((CharactersWindow)await OpenViewerAsync("characters")).Equip(int.Parse(equip[0]), uint.Parse(equip[1])); break;
            case "unequip": ((CharactersWindow)await OpenViewerAsync("characters")).Unequip(int.Parse(argument)); break;
            case "class": ((CharactersWindow)await OpenViewerAsync("characters")).SetClass(argument); break;
            case "fx": ((EffectsWindow)await OpenViewerAsync("effects")).Select(ushort.Parse(argument)); break;
            case "fx-time": ((EffectsWindow)await OpenViewerAsync("effects")).SetTime(float.Parse(argument, CultureInfo.InvariantCulture)); break;
            case "fx-play": ((EffectsWindow)await OpenViewerAsync("effects")).SetPlaying(bool.Parse(argument)); break;
            case "fx-restart": ((EffectsWindow)await OpenViewerAsync("effects")).Restart(); break;
            case "fx-release": ((EffectsWindow)await OpenViewerAsync("effects")).RequestRelease(); break;
            case "fx-hit": ((EffectsWindow)await OpenViewerAsync("effects")).RequestHit(); break;
            case "fx-step": ((EffectsWindow)await OpenViewerAsync("effects")).Step(float.Parse(argument, CultureInfo.InvariantCulture)); break;
            case "fx-cell": ((EffectsWindow)await OpenViewerAsync("effects")).SetAtlasCell(int.Parse(argument)); break;
            case "fx-variant": ((EffectsWindow)await OpenViewerAsync("effects")).SelectVariant(argument == "script" ? null : int.Parse(argument)); break;
            case "fx-strength": ((EffectsWindow)await OpenViewerAsync("effects")).SetStrength(uint.Parse(argument)); break;
            case "fx-distance": ((EffectsWindow)await OpenViewerAsync("effects")).SetDistance(float.Parse(argument, CultureInfo.InvariantCulture)); break;
            case "view": await OpenViewerAsync(argument); break;
            case "screenshot": ((IAssetViewerWindow?)_page ?? throw new InvalidOperationException("Open a viewer first.")).SaveScreenshot(argument.Length == 0 ? "sacred-preview.png" : argument); break;
            default: throw new ArgumentException("Unknown browser cheat.");
        }
        if (_page is IAssetViewerWindow viewer) await viewer.Ready;
        return "ready";
    }
}

public static partial class BrowserCheats
{
    [JSExport]
    public static Task<string> Command(string command) =>
        Dispatcher.UIThread.InvokeAsync(() =>
            (BrowserRoot.Current ?? throw new InvalidOperationException("The viewer has not started.")).ExecuteAsync(command));

    [JSExport]
    public static async Task<string> LoadInstallation(string manifest)
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
            (BrowserRoot.Current ?? throw new InvalidOperationException("The viewer has not started.")).LoadAsync(manifest));
        return "ready";
    }
}

