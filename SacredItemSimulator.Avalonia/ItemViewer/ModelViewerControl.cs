using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Sacred.Assets;
using Sacred.Granny;

namespace SacredItemSimulator.Avalonia.ItemViewer;

public sealed class ModelViewerControl : Control
{
    private const int MaximumDrawTriangles = 60000;
    private const float MinimumZoom = 0.45f;
    private const float MaximumZoom = 3.5f;

    private static readonly IBrush BackgroundBrush = new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
        GradientStops =
        [
            new GradientStop(Color.FromRgb(13, 25, 23), 0),
            new GradientStop(Color.FromRgb(31, 54, 39), 1)
        ]
    };

    private static readonly IPen GridPen = new Pen(new SolidColorBrush(Color.FromArgb(70, 116, 154, 123)), 1);
    private static readonly IPen EdgePen = new Pen(new SolidColorBrush(Color.FromArgb(75, 235, 255, 218)), 0.7);
    private static readonly IBrush TextBrush = new SolidColorBrush(Color.FromRgb(232, 244, 214));
    private static readonly IBrush TextPanelBrush = new SolidColorBrush(Color.FromArgb(170, 9, 17, 14));
    private static readonly Typeface Typeface = new("Consolas");

    private GrnAsset? _asset;
    private MeshViewData? _viewData;
    private IReadOnlyDictionary<string, TextureView> _textures = new Dictionary<string, TextureView>(StringComparer.OrdinalIgnoreCase);
    private string _status = "Select an item to load its model.";
    private Point? _lastPointerPosition;
    private Vector3 _previewRotation;
    private RotationInterpretation _rotationInterpretation = RotationInterpretation.EulerXyz;
    private float _userYaw;
    private float _userPitch;
    private float _zoom = 1.0f;

    public ModelViewerControl()
    {
        ClipToBounds = true;
        Focusable = true;
        PointerPressed += OnPointerPressed;
        PointerMoved += OnPointerMoved;
        PointerReleased += OnPointerReleased;
        PointerWheelChanged += OnPointerWheelChanged;
        KeyDown += OnKeyDown;
    }

    public void ShowModel(GrnAsset asset, Vector3 previewRotation)
    {
        _asset = asset;
        _viewData = asset.Mesh is null ? null : MeshViewData.FromMesh(asset.Mesh);
        _textures = new Dictionary<string, TextureView>(StringComparer.OrdinalIgnoreCase);
        _previewRotation = previewRotation;
        _userYaw = MathF.PI / 2;
        _userPitch = 0.0f;
        _status = asset.Mesh is null
            ? $"{asset.Name}: GRN loaded, no mesh extracted."
            : $"{asset.Name}: {asset.Mesh.Vertices.Length} vertices, {asset.Mesh.Indices.Length / 3} triangles | rot {FormatRotation(previewRotation)}";
        InvalidateVisual();
    }

    public void ShowTextureStatus(string status)
    {
        if (_asset is null)
            _status = status;
        else if (_asset.Mesh is null)
            _status = $"{_asset.Name}: {status}";
        else
            _status = $"{_asset.Name}: {_asset.Mesh.Vertices.Length} vertices, {_asset.Mesh.Indices.Length / 3} triangles | {status}";

        InvalidateVisual();
    }

    public void ShowTextures(IReadOnlyDictionary<string, TextureAsset> textures, int failedCount)
    {
        _textures = textures.ToDictionary(
            static pair => pair.Key,
            static pair => TextureView.FromAsset(pair.Value),
            StringComparer.OrdinalIgnoreCase);

        var total = _viewData?.TextureNames.Count ?? _textures.Count;
        var failedSuffix = failedCount > 0 ? $", {failedCount} failed" : string.Empty;
        ShowTextureStatus($"{_textures.Count}/{total} textures{failedSuffix}");
    }

    public void ShowStatus(string status)
    {
        _asset = null;
        _viewData = null;
        _textures = new Dictionary<string, TextureView>(StringComparer.OrdinalIgnoreCase);
        _previewRotation = Vector3.Zero;
        _userYaw = 0.0f;
        _userPitch = 0.0f;
        _status = status;
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsFinite(availableSize.Width) ? availableSize.Width : 360;
        var height = double.IsFinite(availableSize.Height) ? availableSize.Height : 420;
        return new Size(width, height);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var bounds = Bounds;
        context.FillRectangle(BackgroundBrush, bounds);
        DrawGrid(context, bounds);

        if (_viewData is not null)
            DrawMesh(context, bounds, _viewData);

        DrawStatus(context, bounds);
    }

    private static void DrawGrid(DrawingContext context, Rect bounds)
    {
        const double step = 34;
        for (var x = step; x < bounds.Width; x += step)
            context.DrawLine(GridPen, new Point(x, 0), new Point(x, bounds.Height));

        for (var y = step; y < bounds.Height; y += step)
            context.DrawLine(GridPen, new Point(0, y), new Point(bounds.Width, y));
    }

    private void DrawMesh(DrawingContext context, Rect bounds, MeshViewData viewData)
    {
        if (bounds.Width < 8 || bounds.Height < 8)
            return;

        var itemRotation = CreateItemRotation(_previewRotation, _rotationInterpretation);
        var viewRotation =
            Matrix4x4.CreateRotationX(_userPitch) *
            Matrix4x4.CreateRotationZ(_userYaw);
        var rotation = itemRotation * viewRotation;
        var lightDirection = Vector3.Normalize(new Vector3(-0.35f, -0.65f, 0.95f));
        var cameraDistance = Math.Max(80.0f, viewData.Radius * 3.25f);
        var focalLength = Math.Min(bounds.Width, bounds.Height) * 0.9f * _zoom;
        var screenCenter = new Point(bounds.Width * 0.5, bounds.Height * 0.48);

        var transformed = new ViewVertex[viewData.Positions.Length];
        for (var i = 0; i < viewData.Positions.Length; i++)
        {
            var local = viewData.Positions[i] - viewData.Center;
            var view = Vector3.Transform(local, rotation);
            var depth = view.Y + cameraDistance;
            transformed[i] = new ViewVertex(
                view,
                new Point(
                    screenCenter.X + view.X * focalLength / depth,
                    screenCenter.Y - view.Z * focalLength / depth),
                depth);
        }

        var triangles = new List<RenderTriangle>(Math.Min(viewData.Indices.Length / 3, MaximumDrawTriangles));
        for (var i = 0; i + 2 < viewData.Indices.Length && triangles.Count < MaximumDrawTriangles; i += 3)
        {
            var ia = viewData.Indices[i];
            var ib = viewData.Indices[i + 1];
            var ic = viewData.Indices[i + 2];
            if (ia >= transformed.Length || ib >= transformed.Length || ic >= transformed.Length)
                continue;

            var a = transformed[ia];
            var b = transformed[ib];
            var c = transformed[ic];
            if (a.Depth <= 1 || b.Depth <= 1 || c.Depth <= 1)
                continue;

            var normal = Vector3.Cross(b.ViewPosition - a.ViewPosition, c.ViewPosition - a.ViewPosition);
            if (normal.LengthSquared() <= 0.000001f)
                continue;

            normal = Vector3.Normalize(normal);
            if (normal.Y >= 0.0f)
                continue;

            var shade = Math.Clamp(0.24f + MathF.Max(0.0f, Vector3.Dot(normal, lightDirection)) * 0.76f, 0.18f, 1.0f);
            var triangleIndex = i / 3;
            var baseColor = SampleTriangleColor(viewData, triangleIndex, ia, ib, ic);
            triangles.Add(new RenderTriangle(
                a.Screen,
                b.Screen,
                c.Screen,
                (a.Depth + b.Depth + c.Depth) / 3.0f,
                shade,
                baseColor));
        }

        foreach (var triangle in triangles.OrderByDescending(static t => t.Depth))
            DrawTriangle(context, triangle);
    }

    private static void DrawTriangle(DrawingContext context, RenderTriangle triangle)
    {
        var color = Color.FromRgb(
            ShadeChannel(triangle.BaseColor.R, triangle.Shade),
            ShadeChannel(triangle.BaseColor.G, triangle.Shade),
            ShadeChannel(triangle.BaseColor.B, triangle.Shade));
        var fill = new SolidColorBrush(color);
        var geometry = new StreamGeometry();

        using (var stream = geometry.Open())
        {
            stream.BeginFigure(triangle.A, true);
            stream.LineTo(triangle.B);
            stream.LineTo(triangle.C);
            stream.EndFigure(true);
        }

        context.DrawGeometry(fill, EdgePen, geometry);
    }

    private Color SampleTriangleColor(MeshViewData viewData, int triangleIndex, ushort ia, ushort ib, ushort ic)
    {
        if (triangleIndex >= viewData.TriangleTextureNames.Length)
            return FallbackColor(triangleIndex);

        var textureName = viewData.TriangleTextureNames[triangleIndex];
        if (string.IsNullOrWhiteSpace(textureName) || !_textures.TryGetValue(textureName, out var texture))
            return FallbackColor(triangleIndex);

        var uv = (viewData.TexCoords[ia] + viewData.TexCoords[ib] + viewData.TexCoords[ic]) / 3.0f;
        return texture.Sample(uv);
    }

    private static Color FallbackColor(int triangleIndex)
    {
        var band = triangleIndex % 7;
        return Color.FromRgb(
            (byte)(84 + band * 7),
            (byte)(128 + band * 5),
            (byte)(92 + band * 4));
    }

    private static byte ShadeChannel(byte value, float shade)
    {
        var lit = value * (0.28f + shade * 0.82f) + 18.0f;
        return (byte)Math.Clamp((int)lit, 0, 255);
    }

    private static Matrix4x4 CreateItemRotation(Vector3 rotation, RotationInterpretation interpretation)
    {
        return interpretation switch
        {
            RotationInterpretation.EulerXyz => CreateEulerXyz(rotation),
            RotationInterpretation.EulerZyx => CreateEulerZyx(rotation),
            RotationInterpretation.YawPitchRoll => Matrix4x4.CreateFromYawPitchRoll(rotation.Y, rotation.X, rotation.Z),
            RotationInterpretation.ZOnly => Matrix4x4.CreateRotationZ(rotation.Z),
            RotationInterpretation.AxisAngleVector => CreateAxisAngleVectorRotation(rotation),
            _ => Matrix4x4.Identity
        };
    }

    private static Matrix4x4 CreateEulerXyz(Vector3 rotation)
    {
        return Matrix4x4.CreateRotationX(rotation.X) *
               Matrix4x4.CreateRotationY(rotation.Y) *
               Matrix4x4.CreateRotationZ(rotation.Z);
    }

    private static Matrix4x4 CreateEulerZyx(Vector3 rotation)
    {
        return Matrix4x4.CreateRotationZ(rotation.Z) *
               Matrix4x4.CreateRotationY(rotation.Y) *
               Matrix4x4.CreateRotationX(rotation.X);
    }

    private static Matrix4x4 CreateAxisAngleVectorRotation(Vector3 rotation)
    {
        var angle = rotation.Length();
        return angle < 0.0001f || !float.IsFinite(angle)
            ? Matrix4x4.Identity
            : Matrix4x4.CreateFromAxisAngle(rotation / angle, angle);
    }

    private static string FormatRotation(Vector3 rotation)
    {
        return $"({rotation.X:0.###}, {rotation.Y:0.###}, {rotation.Z:0.###})";
    }

    private static string FormatAngle(float radians)
    {
        return $"{radians:0.###} rad/{RadiansToDegrees(radians):0.#} deg";
    }

    private static float RadiansToDegrees(float radians)
    {
        return radians * 180.0f / MathF.PI;
    }

    private void DrawStatus(DrawingContext context, Rect bounds)
    {
        var help = _asset is null
            ? _status
            : $"{_status}\nmode {_rotationInterpretation} (R cycles)\nuser yaw {FormatAngle(_userYaw)}\npitch {FormatAngle(_userPitch)}\ndrag rotate, wheel zoom";
        var text = new FormattedText(
            help,
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            Typeface,
            13,
            TextBrush);
        var panel = new Rect(8, Math.Max(8, bounds.Height - 140), Math.Min(bounds.Width - 16, text.Width + 18), 82);

        context.FillRectangle(TextPanelBrush, panel, 5);
        context.DrawText(text, new Point(panel.X + 9, panel.Y + 5));
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        Focus();
        _lastPointerPosition = e.GetPosition(this);
        e.Pointer.Capture(this);
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_lastPointerPosition is null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        var position = e.GetPosition(this);
        var delta = position - _lastPointerPosition.Value;
        _lastPointerPosition = position;
        _userYaw += (float)delta.X * 0.01f;
        _userPitch = Math.Clamp(_userPitch + (float)delta.Y * 0.008f, -1.25f, 1.25f);
        InvalidateVisual();
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _lastPointerPosition = null;
        e.Pointer.Capture(null);
    }

    private void OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        _zoom = Math.Clamp(_zoom * (float)Math.Pow(1.12, e.Delta.Y), MinimumZoom, MaximumZoom);
        InvalidateVisual();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.R)
            return;

        _rotationInterpretation = e.KeyModifiers.HasFlag(KeyModifiers.Shift)
            ? PreviousRotationInterpretation(_rotationInterpretation)
            : NextRotationInterpretation(_rotationInterpretation);
        e.Handled = true;
        InvalidateVisual();
    }

    private static RotationInterpretation NextRotationInterpretation(RotationInterpretation value)
    {
        var values = Enum.GetValues<RotationInterpretation>();
        return values[((int)value + 1) % values.Length];
    }

    private static RotationInterpretation PreviousRotationInterpretation(RotationInterpretation value)
    {
        var values = Enum.GetValues<RotationInterpretation>();
        return values[((int)value + values.Length - 1) % values.Length];
    }

    private sealed record MeshViewData(
        Vector3[] Positions,
        Vector2[] TexCoords,
        ushort[] Indices,
        string?[] TriangleTextureNames,
        IReadOnlySet<string> TextureNames,
        Vector3 Center,
        float Radius)
    {
        public static MeshViewData FromMesh(Mesh mesh)
        {
            var positions = mesh.Vertices.Select(static vertex => vertex.Position).ToArray();
            var texCoords = mesh.Vertices.Select(static vertex => vertex.TexCoord).ToArray();
            var min = new Vector3(float.MaxValue);
            var max = new Vector3(float.MinValue);
            foreach (var position in positions)
            {
                min = Vector3.Min(min, position);
                max = Vector3.Max(max, position);
            }

            var center = (min + max) * 0.5f;
            var radius = positions.Length == 0
                ? 1.0f
                : positions.Max(position => Vector3.Distance(position, center));

            return new MeshViewData(
                positions,
                texCoords,
                mesh.Indices,
                BuildTriangleTextureNames(mesh),
                mesh.Surfaces
                    .Select(static surface => surface.TextureName)
                    .Where(static name => !string.IsNullOrWhiteSpace(name))
                    .Select(static name => name!)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase),
                center,
                Math.Max(1.0f, radius));
        }

        private static string?[] BuildTriangleTextureNames(Mesh mesh)
        {
            var triangleTextures = new string?[mesh.Indices.Length / 3];
            foreach (var surface in mesh.Surfaces)
            {
                if (surface.IndexCount <= 0 || string.IsNullOrWhiteSpace(surface.TextureName))
                    continue;

                var start = Math.Max(0, surface.IndexStart / 3);
                var end = Math.Min(triangleTextures.Length, (surface.IndexStart + surface.IndexCount + 2) / 3);
                for (var triangle = start; triangle < end; triangle++)
                    triangleTextures[triangle] = surface.TextureName;
            }

            return triangleTextures;
        }
    }

    private sealed record TextureView(string Name, int Width, int Height, byte[] Rgba)
    {
        public static TextureView FromAsset(TextureAsset asset) => new(asset.Name, asset.Width, asset.Height, asset.Rgba8);

        public Color Sample(Vector2 uv)
        {
            var u = Repeat(uv.X);
            var v = Repeat(uv.Y);
            var x = Math.Clamp((int)(u * Width), 0, Width - 1);
            var y = Math.Clamp((int)(v * Height), 0, Height - 1);
            var offset = (y * Width + x) * 4;
            if (offset < 0 || offset + 3 >= Rgba.Length || Rgba[offset + 3] < 8)
                return Color.FromRgb(126, 151, 105);

            return Color.FromRgb(Rgba[offset], Rgba[offset + 1], Rgba[offset + 2]);
        }

        private static float Repeat(float value)
        {
            if (!float.IsFinite(value))
                return 0.0f;

            value -= MathF.Floor(value);
            return value < 0.0f ? value + 1.0f : value;
        }
    }

    private readonly record struct ViewVertex(Vector3 ViewPosition, Point Screen, float Depth);

    private readonly record struct RenderTriangle(Point A, Point B, Point C, float Depth, float Shade, Color BaseColor);

    private enum RotationInterpretation
    {
        EulerXyz,
        EulerZyx,
        YawPitchRoll,
        ZOnly,
        AxisAngleVector,
    }
}
