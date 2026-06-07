using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Sacred.Assets;
using Sacred.Granny;

namespace SacredItemSimulator.Avalonia.ItemViewer;

public sealed class ModelViewerControl : UserControl
{
    private readonly Dx12ModelViewportHost _viewport = new();
    private readonly TextBlock _statusText;
    private GrnAsset? _asset;
    private string _status = "Select an item to load its model.";
    private Vector3 _previewRotation;
    private int _gridWidth = 1;
    private int _gridHeight = 1;
    private float _userYaw;
    private float _userPitch;
    private float _userRoll;

    public ModelViewerControl()
    {
        ClipToBounds = true;
        Focusable = true;

        _statusText = new TextBlock
        {
            Text = "Select an item to load its model.",
            Foreground = new SolidColorBrush(Color.FromRgb(232, 244, 214)),
            Background = new SolidColorBrush(Color.FromArgb(210, 9, 17, 14)),
            FontFamily = new FontFamily("Consolas"),
            FontSize = 13,
            Padding = new Thickness(8, 5),
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Bottom,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        var root = new Grid
        {
            RowDefinitions = new RowDefinitions("*,Auto"),
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.FromRgb(13, 25, 23), 0),
                    new GradientStop(Color.FromRgb(31, 54, 39), 1)
                }
            }
        };
        root.Children.Add(_viewport);
        Grid.SetRow(_viewport, 0);
        root.Children.Add(_statusText);
        Grid.SetRow(_statusText, 1);

        Content = root;
        PointerPressed += (_, _) => Focus();
    }

    public void ClearModel()
    {
        RunOnUiThread(() =>
        {
            _asset = null;
            _previewRotation = Vector3.Zero;
            _gridWidth = 1;
            _gridHeight = 1;
            _viewport.ClearModel();
            SetStatusText("Select an item to load its model.");
        });
    }

    public void ShowModel(GrnAsset asset, Vector3 previewRotation, int gridWidth, int gridHeight)
    {
        RunOnUiThread(() =>
        {
            _asset = asset;
            _previewRotation = previewRotation;
            _gridWidth = Math.Clamp(gridWidth, 1, 4);
            _gridHeight = Math.Clamp(gridHeight, 1, 5);
            _viewport.ShowModel(asset, previewRotation, _gridWidth, _gridHeight);
            SetStatusText(asset.Mesh is null
                ? $"{asset.Name}: GRN loaded, no mesh extracted."
                : $"{asset.Name}: {asset.Mesh.Vertices.Length} vertices, {asset.Mesh.Indices.Length / 3} triangles | {_gridWidth}x{_gridHeight} cells | rot {FormatRotation(previewRotation)}");
        });
    }

    public void SetUserRotation(float yaw, float pitch, float roll)
    {
        RunOnUiThread(() =>
        {
            _userYaw = yaw;
            _userPitch = pitch;
            _userRoll = roll;
            _viewport.SetUserRotation(yaw, pitch, roll);
            SetStatusText(_status);
        });
    }

    public void ShowTextureStatus(string status)
    {
        RunOnUiThread(() =>
        {
            if (_asset is null)
                SetStatusText(status);
            else if (_asset.Mesh is null)
                SetStatusText($"{_asset.Name}: {status}");
            else
                SetStatusText($"{_asset.Name}: {_asset.Mesh.Vertices.Length} vertices, {_asset.Mesh.Indices.Length / 3} triangles | {status}");
        });
    }

    public void ShowTextures(IReadOnlyDictionary<string, TextureAsset> textures, int failedCount)
    {
        RunOnUiThread(() =>
        {
            _viewport.ShowTextures(textures);
            var total = _asset?.Mesh?.Surfaces
                .Select(static surface => surface.TextureName)
                .Where(static name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count() ?? textures.Count;
            var failedSuffix = failedCount > 0 ? $", {failedCount} failed" : string.Empty;
            ShowTextureStatus($"{textures.Count}/{total} textures{failedSuffix}");
        });
    }

    public void ShowStatus(string status)
    {
        RunOnUiThread(() =>
        {
            _asset = null;
            _previewRotation = Vector3.Zero;
            _gridWidth = 1;
            _gridHeight = 1;
            _viewport.ClearModel();
            SetStatusText(status);
        });
    }

    private void SetStatusText(string status)
    {
        _status = status;
        _statusText.Text = _asset is null
            ? status
            : $"{status}\ngrid {_gridWidth}x{_gridHeight}\npreview {FormatRotationWithDegrees(_previewRotation)}\nuser yaw {FormatAngle(_userYaw)}\npitch {FormatAngle(_userPitch)}\nroll {FormatAngle(_userRoll)}";
    }

    private void RunOnUiThread(Action action)
    {
        if (Dispatcher.CheckAccess())
            action();
        else
            Dispatcher.UIThread.Post(action, DispatcherPriority.Normal);
    }

    private static string FormatRotation(Vector3 rotation)
    {
        return $"({rotation.X:0.###}, {rotation.Y:0.###}, {rotation.Z:0.###})";
    }

    private static string FormatRotationWithDegrees(Vector3 rotation)
    {
        return $"{FormatRotation(rotation)} rad / ({RadiansToDegrees(rotation.X):0.#}, {RadiansToDegrees(rotation.Y):0.#}, {RadiansToDegrees(rotation.Z):0.#}) deg";
    }

    private static string FormatAngle(float radians)
    {
        return $"{radians:0.000###} rad/{RadiansToDegrees(radians):0.#} deg";
    }

    private static float RadiansToDegrees(float radians)
    {
        return radians * 180.0f / MathF.PI;
    }
}
